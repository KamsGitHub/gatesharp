using System.Collections.Frozen;
using System.Net.Http.Headers;
using Microsoft.Extensions.Primitives;

namespace GateSharp.Proxy;

/// <summary>
/// Which headers cross the gateway and which don't. The policy is described in ADR 0002.
/// </summary>
internal static class HeaderRules
{
    // Hop-by-hop headers (RFC 9110 section 7.6.1) describe one connection and never cross a proxy.
    // Stripping Upgrade also blocks h2c upgrade smuggling; WebSockets get explicit support later.
    private static readonly string[] HopByHop =
        ["Connection", "Keep-Alive", "Proxy-Connection", "TE", "Trailer", "Transfer-Encoding", "Upgrade"];

    // FrozenSet is built once and optimized for lookups, which is all the hot path does with it.
    private static readonly FrozenSet<string> RequestSkip = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        [
            .. HopByHop,
            "Proxy-Authorization",
            // The upstream gets its own authority as Host. The client's Host goes in X-Forwarded-Host.
            "Host",
            // Kestrel already answered 100-continue when the body was first read.
            "Expect",
            // Client-supplied forwarding headers are untrusted and get replaced, not appended to.
            "Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Prefix", "X-Real-IP",
        ]);

    private static readonly FrozenSet<string> ResponseSkip = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        [.. HopByHop, "Proxy-Authenticate"]);

    public static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        var connection = source.Headers.Connection.ToString();

        foreach (var (name, values) in source.Headers)
        {
            if (RequestSkip.Contains(name) || IsNominatedBy(connection, name))
            {
                continue;
            }

            // HttpRequestMessage keeps Content-* headers on its content object, and refuses them on
            // the request itself. Headers that end up nowhere (Content-Type on a GET) are dropped.
            if (!AddTo(target.Headers, name, values) && target.Content is not null)
            {
                AddTo(target.Content.Headers, name, values);
            }
        }

        // Added after the copy loop, so a client cannot name them in Connection to get them removed.
        if (source.HttpContext.Connection.RemoteIpAddress is { } clientIp)
        {
            target.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp.ToString());
        }

        target.Headers.TryAddWithoutValidation("X-Forwarded-Proto", source.Scheme);
        if (source.Host.HasValue)
        {
            target.Headers.TryAddWithoutValidation("X-Forwarded-Host", source.Host.Value);
        }
    }

    public static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
    {
        // NonValidated gives the raw strings as received, without HttpClient parsing each header
        // into typed values that we would only turn back into strings.
        var connection = source.Headers.NonValidated.TryGetValues("Connection", out var c) ? c.ToString() : "";

        CopyResponseHeaders(source.Headers.NonValidated, connection, target);
        CopyResponseHeaders(source.Content.Headers.NonValidated, connection, target);
    }

    private static void CopyResponseHeaders(HttpHeadersNonValidated source, string connection, HttpResponse target)
    {
        foreach (var (name, values) in source)
        {
            if (!ResponseSkip.Contains(name) && !IsNominatedBy(connection, name))
            {
                target.Headers[name] = ToStringValues(values);
            }
        }
    }

    /// <summary>True if <paramref name="name"/> is listed in a Connection header value such as "close, x-foo".</summary>
    internal static bool IsNominatedBy(string connection, string name)
    {
        var span = connection.AsSpan();
        foreach (var range in span.Split(','))
        {
            if (span[range].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AddTo(HttpHeaders headers, string name, StringValues values) =>
        values.Count == 1
            ? headers.TryAddWithoutValidation(name, values.ToString())
            : headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);

    private static StringValues ToStringValues(HeaderStringValues values)
    {
        if (values.Count == 1)
        {
            foreach (var value in values)
            {
                return value;
            }
        }

        // Several values (Set-Cookie, for example) must stay separate, not be joined with commas.
        var array = new string[values.Count];
        var i = 0;
        foreach (var value in values)
        {
            array[i++] = value;
        }

        return array;
    }
}
