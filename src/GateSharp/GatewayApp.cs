using GateSharp.Proxy;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace GateSharp;

/// <summary>
/// Builds the gateway host. Program.cs and the integration tests both go through here,
/// so tests exercise the same Kestrel configuration that production uses.
/// </summary>
public static class GatewayApp
{
    public static WebApplication Build(string[] args)
    {
        // The slim builder leaves out HTTPS, host filtering and other defaults we don't want
        // on the hot path. Each feature gets added back explicitly when a milestone needs it.
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Don't advertise the server implementation to untrusted clients.
            kestrel.AddServerHeader = false;

            // HTTP/1.1 only until Milestone 2+ adds h2c deliberately.
            kestrel.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http1);

            // Tighter than Kestrel's defaults (30s / 32 KB / 100 headers): slow-header
            // (Slowloris-style) clients and oversized header blocks get rejected early.
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            kestrel.Limits.MaxRequestHeaderCount = 64;
        });

        // Registered with a factory, not as an instance: the container only disposes singletons it
        // created itself, and disposing the invoker is what closes the pooled upstream connections.
        builder.Services.AddSingleton(_ => UpstreamClient.Create());

        var app = builder.Build();

        // Read and validated at startup: a bad upstream URL stops the gateway from starting,
        // rather than turning into a 502 on every request.
        var upstream = ReadUpstream(app.Configuration);
        var forwarder = upstream is null
            ? null
            : new RequestForwarder(
                app.Services.GetRequiredService<HttpMessageInvoker>(),
                upstream,
                app.Services.GetRequiredService<ILogger<RequestForwarder>>());

        // A single terminal handler instead of ASP.NET Core endpoint routing. The gateway owns
        // the whole URL space, and Milestone 3 adds GateSharp's own router inside it.
        app.Run(new GatewayHandler(forwarder).HandleAsync);

        return app;
    }

    private static Uri? ReadUpstream(IConfiguration configuration)
    {
        var value = configuration["Gateway:Upstream"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException(
                $"Gateway:Upstream must be an absolute http:// URL with no query, fragment or credentials. Got '{value}'.");
        }

        return uri;
    }
}
