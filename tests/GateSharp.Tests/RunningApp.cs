using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace GateSharp.Tests;

/// <summary>
/// Starts a real Kestrel app on an ephemeral loopback port. We deliberately avoid TestServer:
/// a gateway's correctness depends on real sockets and real HTTP framing, which TestServer skips.
/// </summary>
internal sealed class RunningApp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private RunningApp(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
        Client = new HttpClient { BaseAddress = baseAddress };
    }

    public Uri BaseAddress { get; }
    public HttpClient Client { get; }

    public static async Task<RunningApp> StartAsync(Func<string[], WebApplication> build, params string[] extraArgs)
    {
        // Port 0 lets the OS choose a free port, so tests can run in parallel.
        string[] args = ["--Kestrel:Endpoints:Data:Url=http://127.0.0.1:0", "--urls=http://127.0.0.1:0", .. extraArgs];
        var app = build(args);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var address = app.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single();
        return new RunningApp(app, new Uri(address));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
