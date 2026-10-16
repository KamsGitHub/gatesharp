namespace GateSharp;

internal static class GatewayHandler
{
    // Encoded once at startup. A u8 literal is UTF-8 bytes produced by the compiler,
    // so the response path does no JSON serialization and no string encoding.
    private static readonly byte[] NoRouteBody =
        """{"error":"no_route","message":"No route matches this request."}"""u8.ToArray();

    public static Task HandleAsync(HttpContext context)
    {
        // Nothing is routed yet, so every request gets the gateway's own 404.
        // That keeps it distinct from a 404 an upstream would return.
        return WriteErrorAsync(context, StatusCodes.Status404NotFound, NoRouteBody);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, byte[] body)
    {
        var response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength = body.Length;
        response.Headers.CacheControl = "no-store";

        // BodyWriter is Kestrel's PipeWriter, so this copies straight into its output buffers.
        await response.BodyWriter.WriteAsync(body, context.RequestAborted);
    }
}
