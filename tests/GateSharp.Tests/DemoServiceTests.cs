using System.Net;
using System.Net.Http.Json;
using GateSharp.DemoService;

namespace GateSharp.Tests;

public sealed class DemoServiceTests
{
    [Fact]
    public async Task Echoes_method_path_query_and_body_size()
    {
        await using var demo = await RunningApp.StartAsync(DemoServiceApp.Build, "--name=users-1");
        var ct = TestContext.Current.CancellationToken;

        using var content = new ByteArrayContent(new byte[100_000]);
        using var response = await demo.Client.PostAsync("/users/42?verbose=true", content, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<EchoResponse>(ct);
        Assert.NotNull(echo);
        Assert.Equal("users-1", echo.Service);
        Assert.Equal("POST", echo.Method);
        Assert.Equal("/users/42", echo.Path);
        Assert.Equal("?verbose=true", echo.Query);
        Assert.Equal(100_000, echo.BodyBytes);
    }

    [Fact]
    public async Task Status_query_parameter_overrides_status_code()
    {
        await using var demo = await RunningApp.StartAsync(DemoServiceApp.Build);

        using var response = await demo.Client.GetAsync("/?status=503", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
