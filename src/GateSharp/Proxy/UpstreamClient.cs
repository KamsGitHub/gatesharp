using System.Net;

namespace GateSharp.Proxy;

/// <summary>
/// Creates the single upstream HTTP client the gateway uses for its whole lifetime.
/// </summary>
internal static class UpstreamClient
{
    public static HttpMessageInvoker Create()
    {
        // SocketsHttpHandler owns the connection pool. It has to be long-lived: a new handler per
        // request would open a new TCP connection per request and leak sockets in TIME_WAIT.
        var handler = new SocketsHttpHandler
        {
            // A gateway forwards what it gets. It must not follow redirects, decompress bodies,
            // keep a cookie jar shared by all clients, or route through a system proxy.
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,

            // Otherwise the handler injects its own traceparent header whenever an Activity exists.
            // Tracing is designed deliberately in Milestone 7.
            ActivityHeadersPropagator = null,

            // Fail fast on a dead backend instead of waiting for the OS TCP timeout (minutes).
            ConnectTimeout = TimeSpan.FromSeconds(5),

            // Recycling connections makes DNS changes take effect: a pooled connection keeps the
            // IP it resolved when it was opened. Milestone 4 makes these configurable.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
        };

        // HttpMessageInvoker instead of HttpClient: HttpClient adds a 100 s default timeout,
        // BaseAddress and default headers, none of which a proxy wants. Timeouts come from
        // cancellation tokens the gateway controls.
        return new HttpMessageInvoker(handler, disposeHandler: true);
    }
}
