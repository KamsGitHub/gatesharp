# ADR 0002: Header forwarding policy

**Status:** accepted (Milestone 2)

## Context

The gateway sits between untrusted clients and services that often trust whatever the gateway
sends them. Any header the gateway passes on unchanged becomes a statement made by the gateway.
A copy-everything proxy lets clients spoof their IP address and host to upstream services, and
lets them smuggle connection-level instructions (`Upgrade: h2c`, `Transfer-Encoding`) to them.

## Decision

Implemented in `src/GateSharp/Proxy/HeaderRules.cs`.

- **Hop-by-hop headers are dropped in both directions:** `Connection`, `Keep-Alive`,
  `Proxy-Connection`, `TE`, `Trailer`, `Transfer-Encoding` and `Upgrade` (RFC 9110 section 7.6.1).
  Headers named in the `Connection` header are dropped too. The same applies to `Proxy-Authorization`
  on requests and `Proxy-Authenticate` on responses.
  Framing is rebuilt on each side: Kestrel decides how to frame the response to the client, and
  `HttpClient` decides how to frame the request to the upstream.
- **`Host` is rewritten** to the upstream's authority. The original value goes in `X-Forwarded-Host`.
- **Forwarding headers are replaced, never appended to.** Any `Forwarded`, `X-Forwarded-*` or
  `X-Real-IP` the client sent is discarded. The gateway then sets `X-Forwarded-For` (the TCP peer
  address), `X-Forwarded-Proto` and `X-Forwarded-Host` after the copy, so a client cannot name them
  in `Connection` to get them removed.
- **`Expect` is not forwarded.** Kestrel answers `100-continue` itself when the body is first read.
- **Everything else is forwarded unchanged.** `HttpHeaders.NonValidated` is used on responses so
  values are not parsed and reformatted, and multi-value headers such as `Set-Cookie` stay separate.

## Consequences

- With no trusted-proxy list, a GateSharp behind a load balancer reports the load balancer's IP in
  `X-Forwarded-For`. A trusted-proxy list (appending only when the TCP peer is trusted) gets added
  when deployment needs it. Trusting by default would be the unsafe choice.
- WebSockets and other `Upgrade` flows don't work until they get explicit support.
- Request and response trailers are not forwarded yet.
- Upstream `Server` headers pass through. Hiding backend details is a hardening item for Milestone 7.
