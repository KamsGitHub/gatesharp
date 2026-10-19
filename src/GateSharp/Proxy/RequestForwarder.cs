using System.Net;
using Microsoft.AspNetCore.Http.Features;

namespace GateSharp.Proxy;

/// <summary>
/// Forwards one request to one upstream and streams the response back.
/// </summary>
internal sealed partial class RequestForwarder(HttpMessageInvoker client, Uri upstream, ILogger<RequestForwarder> logger)
{
    // "http://host:port/prefix" with no trailing slash. The request path, which always starts with '/', is appended.
    private readonly string _targetPrefix = upstream.GetLeftPart(UriPartial.Path).TrimEnd('/');

    public async Task ForwardAsync(HttpContext context)
    {
        // RequestAborted fires when the client disconnects. Every await below takes it, so a client
        // that goes away also cancels the upstream request and frees its connection.
        // (In Tokio, dropping the future would do this implicitly. In .NET it has to be passed down.)
        var ct = context.RequestAborted;

        using var request = CreateUpstreamRequest(context);

        HttpResponseMessage response;
        try
        {
            // The invoker returns once the status line and headers have arrived, and the body stays
            // unread on the socket. (HttpClient would buffer the whole body by default, unless
            // called with HttpCompletionOption.ResponseHeadersRead.)
            response = await client.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // The client is gone. There is nobody to answer.
        }
        catch (OperationCanceledException ex)
        {
            LogUpstreamTimeout(logger, upstream, ex);
            await GatewayErrors.WriteAsync(context, StatusCodes.Status504GatewayTimeout, GatewayErrors.UpstreamTimeout);
            return;
        }
        catch (HttpRequestException ex) when (FindBadRequest(ex) is { } badRequest)
        {
            // Reading the client's body failed (malformed chunking, body over the size limit).
            // That is the client's fault, so it must not be reported as a bad gateway.
            await GatewayErrors.WriteAsync(context, badRequest.StatusCode, GatewayErrors.BadRequest);
            return;
        }
        catch (HttpRequestException ex)
        {
            LogUpstreamFailed(logger, upstream, ex);
            await GatewayErrors.WriteAsync(context, StatusCodes.Status502BadGateway, GatewayErrors.UpstreamUnavailable);
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            HeaderRules.CopyResponseHeaders(response, context.Response);

            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(ct);

                // Kestrel flushes each write to the socket, so chunks reach the client as they
                // arrive from the upstream. Server-sent events and long downloads stream through.
                await body.CopyToAsync(context.Response.Body, ct);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
            {
                if (!ct.IsCancellationRequested)
                {
                    LogUpstreamBodyFailed(logger, upstream, ex);
                }

                // The status line has already gone out, so no error response is possible. Aborting
                // resets the connection, and the client sees a failed transfer instead of a short
                // body it might take for a complete one.
                context.Abort();
            }
        }
    }

    private HttpRequestMessage CreateUpstreamRequest(HttpContext context)
    {
        var source = context.Request;

        // ToUriComponent re-escapes what Kestrel decoded. Kestrel has already rejected malformed
        // targets and removed "." and ".." segments, so the path can't climb above the upstream prefix.
        var target = string.Concat(
            _targetPrefix,
            source.PathBase.ToUriComponent(),
            source.Path.ToUriComponent(),
            source.QueryString.ToUriComponent());

        var request = new HttpRequestMessage(new HttpMethod(source.Method), new Uri(target, UriKind.Absolute))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        // Kestrel knows from the framing (Content-Length or chunked) whether a body follows.
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            // StreamContent reads the client's body as HttpClient writes it upstream: memory use
            // stays flat whatever the body size. With no Content-Length it goes upstream chunked.
            request.Content = new StreamContent(source.Body);
        }

        HeaderRules.CopyRequestHeaders(source, request);
        return request;
    }

    private static BadHttpRequestException? FindBadRequest(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is BadHttpRequestException badRequest)
            {
                return badRequest;
            }
        }

        return null;
    }

    // [LoggerMessage] generates the logging code at compile time: no params array, no boxing,
    // and nothing is formatted when the level is disabled.
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Request to upstream {Upstream} failed")]
    private static partial void LogUpstreamFailed(ILogger logger, Uri upstream, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Request to upstream {Upstream} timed out")]
    private static partial void LogUpstreamTimeout(ILogger logger, Uri upstream, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Upstream {Upstream} failed while streaming the response body")]
    private static partial void LogUpstreamBodyFailed(ILogger logger, Uri upstream, Exception exception);
}
