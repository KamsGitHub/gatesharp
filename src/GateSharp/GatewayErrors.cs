namespace GateSharp;

/// <summary>
/// The gateway's own error responses. Each has an "error" code, so clients and tests can tell them
/// apart from an upstream that happens to return the same status.
/// </summary>
internal static class GatewayErrors
{
    // Encoded once at startup. A u8 literal is UTF-8 bytes produced by the compiler,
    // so the response path does no JSON serialization and no string encoding.
    public static readonly byte[] NoRoute =
        """{"error":"no_route","message":"No route matches this request."}"""u8.ToArray();

    public static readonly byte[] UpstreamUnavailable =
        """{"error":"upstream_unavailable","message":"The upstream service could not be reached."}"""u8.ToArray();

    public static readonly byte[] UpstreamTimeout =
        """{"error":"upstream_timeout","message":"The upstream service did not respond in time."}"""u8.ToArray();

    public static readonly byte[] BadRequest =
        """{"error":"bad_request","message":"The request body could not be read."}"""u8.ToArray();

    public static async Task WriteAsync(HttpContext context, int statusCode, byte[] body)
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
