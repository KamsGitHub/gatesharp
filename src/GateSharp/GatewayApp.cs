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

        var app = builder.Build();

        // A single terminal handler instead of ASP.NET Core endpoint routing. The gateway owns
        // the whole URL space, and Milestone 3 replaces this with GateSharp's own router.
        app.Run(GatewayHandler.HandleAsync);

        return app;
    }
}
