using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using GateSharp.DemoService;

namespace GateSharp.Tests;

/// <summary>
/// Client -> gateway -> demo service, all on real loopback sockets.
/// </summary>
public sealed class ProxyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(RunningApp Demo, RunningApp Gateway)> StartAsync()
    {
        var demo = await RunningApp.StartAsync(DemoServiceApp.Build, "--name=users-1");
        var gateway = await RunningApp.StartAsync(GatewayApp.Build, $"--Gateway:Upstream={demo.BaseAddress}");
        return (demo, gateway);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forwards_method_path_query_and_streams_request_body(bool chunked)
    {
        var (demo, gateway) = await StartAsync();
        await using var _ = demo;
        await using var __ = gateway;

        using var request = new HttpRequestMessage(HttpMethod.Put, "/users/a%20b?verbose=true&x=%26");
        request.Content = new ByteArrayContent(new byte[5_000_000]);
        request.Headers.TransferEncodingChunked = chunked;
        using var response = await gateway.Client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<EchoResponse>(Ct);
        Assert.NotNull(echo);
        Assert.Equal("users-1", echo.Service);
        Assert.Equal("PUT", echo.Method);
        Assert.Equal("/users/a b", echo.Path);
        Assert.Equal("?verbose=true&x=%26", echo.Query);
        Assert.Equal(5_000_000, echo.BodyBytes);
    }

    [Fact]
    public async Task Upstream_status_and_end_to_end_headers_pass_through()
    {
        var (demo, gateway) = await StartAsync();
        await using var _ = demo;
        await using var __ = gateway;

        using var response = await gateway.Client.GetAsync("/orders?status=503", Ct);

        // The upstream's 503 is passed on as-is. It is not the gateway's own error.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("users-1", response.Headers.GetValues("x-demo-service").Single());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Hop_by_hop_and_connection_nominated_headers_are_not_forwarded()
    {
        var (demo, gateway) = await StartAsync();
        await using var _ = demo;
        await using var __ = gateway;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Connection.Add("x-hop");
        request.Headers.Add("x-hop", "connection-scoped");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic Zm9vOmJhcg==");
        request.Headers.Add("x-end-to-end", "kept");
        using var response = await gateway.Client.SendAsync(request, Ct);

        var headers = (await response.Content.ReadFromJsonAsync<EchoResponse>(Ct))!.Headers;
        Assert.Equal("kept", headers["x-end-to-end"]);
        Assert.False(headers.ContainsKey("x-hop"));
        Assert.False(headers.ContainsKey("Keep-Alive"));
        Assert.False(headers.ContainsKey("Proxy-Authorization"));
    }

    [Fact]
    public async Task Forwarding_headers_from_the_client_are_replaced()
    {
        var (demo, gateway) = await StartAsync();
        await using var _ = demo;
        await using var __ = gateway;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-For", "6.6.6.6");
        request.Headers.Add("X-Forwarded-Host", "evil.example");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("Forwarded", "for=6.6.6.6");
        request.Headers.Add("X-Real-IP", "6.6.6.6");
        // A client must not be able to strip the gateway's own forwarding headers via Connection.
        request.Headers.Connection.Add("X-Forwarded-For");
        using var response = await gateway.Client.SendAsync(request, Ct);

        var headers = (await response.Content.ReadFromJsonAsync<EchoResponse>(Ct))!.Headers;
        Assert.Equal("127.0.0.1", headers["X-Forwarded-For"]);
        Assert.Equal(gateway.BaseAddress.Authority, headers["X-Forwarded-Host"]);
        Assert.Equal("http", headers["X-Forwarded-Proto"]);
        Assert.Equal(demo.BaseAddress.Authority, headers["Host"]);
        Assert.False(headers.ContainsKey("Forwarded"));
        Assert.False(headers.ContainsKey("X-Real-IP"));
    }

    [Fact]
    public async Task Response_body_is_streamed_as_upstream_produces_it()
    {
        var (demo, gateway) = await StartAsync();
        await using var _ = demo;
        await using var __ = gateway;

        // The demo service sends "chunk 0", then waits 3 s before "chunk 1".
        var stopwatch = Stopwatch.StartNew();
        using var response = await gateway.Client.GetAsync(
            "/events?chunks=2&chunk_delay_ms=3000", HttpCompletionOption.ResponseHeadersRead, Ct);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        Assert.Equal("chunk 0", await reader.ReadLineAsync(Ct));
        var firstChunkAt = stopwatch.Elapsed;
        Assert.Equal("chunk 1", await reader.ReadLineAsync(Ct));

        // A buffering gateway would hold the first chunk back until the whole body was in.
        Assert.True(firstChunkAt < TimeSpan.FromSeconds(2), $"first chunk arrived after {firstChunkAt}");
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(2.5), $"whole body arrived after {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Unreachable_upstream_gets_gateway_502_json()
    {
        await using var gateway = await RunningApp.StartAsync(
            GatewayApp.Build, $"--Gateway:Upstream=http://127.0.0.1:{UnusedPort()}");

        using var response = await gateway.Client.GetAsync("/users/42", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("upstream_unavailable", json.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("ftp://127.0.0.1:9001")]
    [InlineData("http://127.0.0.1:9001/?q=1")]
    [InlineData("http://user:pass@127.0.0.1:9001")]
    [InlineData("not a url")]
    public void Invalid_upstream_url_stops_the_gateway_from_starting(string upstream)
    {
        Assert.Throws<InvalidOperationException>(() => GatewayApp.Build([$"--Gateway:Upstream={upstream}"]));
    }

    // Bind to port 0, note the port the OS picked, and release it. Nothing listens there afterwards.
    private static int UnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
