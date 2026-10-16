using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;

namespace GateSharp.DemoService;

/// <summary>
/// A fake upstream service. It echoes back what it received so tests can see exactly what the
/// gateway forwarded. Query knobs: <c>?delay_ms=N</c> waits before responding, and
/// <c>?status=CODE</c> sets the response status.
///
/// Run: <c>dotnet run --project tools/GateSharp.DemoService -- --name users-1 --urls http://127.0.0.1:9001</c>
/// </summary>
public static class DemoServiceApp
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        var app = builder.Build();
        var name = app.Configuration["name"] ?? "demo";

        app.Run(context => HandleAsync(context, name));
        return app;
    }

    private static async Task HandleAsync(HttpContext context, string name)
    {
        var request = context.Request;
        var ct = context.RequestAborted;

        if (TryGetInt(request.Query["delay_ms"], out var delayMs) && delayMs > 0)
        {
            // Task.Delay frees the thread while it waits, and the token cancels it when the client disconnects.
            await Task.Delay(Math.Min(delayMs, 60_000), ct);
        }

        // Read the body through without buffering it, counting bytes, so tests can check streaming.
        long bodyBytes = 0;
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, ct)) > 0)
        {
            bodyBytes += read;
        }

        var echo = new EchoResponse(
            Service: name,
            Method: request.Method,
            Path: request.Path.Value ?? "/",
            Query: request.QueryString.Value ?? "",
            Headers: request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            BodyBytes: bodyBytes);

        context.Response.StatusCode =
            TryGetInt(request.Query["status"], out var status) && status is >= 200 and <= 599 ? status : 200;
        context.Response.Headers["x-demo-service"] = name;
        await context.Response.WriteAsJsonAsync(echo, DemoJsonContext.Default.EchoResponse, cancellationToken: ct);
    }

    private static bool TryGetInt(StringValues value, out int result) =>
        int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

public sealed record EchoResponse(
    string Service,
    string Method,
    string Path,
    string Query,
    Dictionary<string, string> Headers,
    long BodyBytes);

// Source-generated serializer: no reflection at runtime, and it stays trim/AOT friendly.
[JsonSerializable(typeof(EchoResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DemoJsonContext : JsonSerializerContext;
