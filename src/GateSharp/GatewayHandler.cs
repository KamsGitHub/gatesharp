using GateSharp.Proxy;

namespace GateSharp;

internal sealed class GatewayHandler(RequestForwarder? forwarder)
{
    // Milestone 2 has a single upstream for everything. Milestone 3 puts the router here.
    // With no upstream configured, every request gets the gateway's own 404, which keeps it
    // distinct from a 404 an upstream would return.
    public Task HandleAsync(HttpContext context) =>
        forwarder is null
            ? GatewayErrors.WriteAsync(context, StatusCodes.Status404NotFound, GatewayErrors.NoRoute)
            : forwarder.ForwardAsync(context);
}
