# ADR 0001: Own proxy core on top of Kestrel, with no YARP and no endpoint routing

**Status:** accepted (Milestone 1)

## Context

The goal is to learn and measure gateway internals. YARP would provide routing, forwarding,
header transforms, load balancing and health checks out of the box, which defeats that goal.
Writing an HTTP/1.1 parser would be the opposite mistake: a large surface for request-smuggling
bugs that teaches little about gateway design.

## Decision

- Kestrel does connection handling and HTTP parsing and framing. We configure its limits explicitly.
- GateSharp owns everything from the parsed request onwards: routing, the forwarding pipeline,
  header rules, upstream pooling policy (on `SocketsHttpHandler`) and resilience.
- The request pipeline is one terminal handler (`app.Run`), not ASP.NET Core endpoint routing or
  a stack of middleware. The gateway owns the whole URL space, and the phase order (auth before rate
  limiting, and so on) is part of the security model, so it is fixed in code rather than assembled
  at startup. The same decision was made in RustGate.

## Consequences

- We must handle hop-by-hop headers, `Connection`-nominated headers, forwarded headers and body
  streaming ourselves, and test them.
- YARP remains a useful reference point: Milestone 7 can benchmark GateSharp against it.
