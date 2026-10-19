using System.Net;
using System.Text.Json;

namespace GateSharp.Tests;

public sealed class GatewaySmokeTests
{
    // An empty value overrides the upstream from appsettings.json, leaving nothing to forward to.
    private static Task<RunningApp> StartGatewayWithoutUpstreamAsync() =>
        RunningApp.StartAsync(GatewayApp.Build, "--Gateway:Upstream=");

    [Fact]
    public async Task Unrouted_request_gets_gateway_404_json()
    {
        await using var gateway = await StartGatewayWithoutUpstreamAsync();

        using var response = await gateway.Client.GetAsync("/anything?x=1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("no_route", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Server_header_is_not_sent_and_protocol_is_http11()
    {
        await using var gateway = await StartGatewayWithoutUpstreamAsync();

        using var response = await gateway.Client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Empty(response.Headers.Server);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact]
    public async Task Oversized_header_block_is_rejected_by_kestrel()
    {
        await using var gateway = await StartGatewayWithoutUpstreamAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("x-big", new string('a', 20 * 1024));
        using var response = await gateway.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestHeaderFieldsTooLarge, response.StatusCode);
    }
}
