# GateSharp

An API gateway and reverse proxy written in C# on .NET 10 and Kestrel. Routing, forwarding,
connection management and resilience are implemented by hand (no YARP). The project exists to
understand and measure the design choices behind production gateways, not to replace them. It
is the .NET sibling of [RustGate](https://github.com/KamsGitHub/rustgate).

> **Status: Milestone 2 (basic reverse proxy).** This README only describes behaviour that is implemented,
> and will only quote benchmark numbers produced by scripts in this repo.

## What works today

- A Kestrel host restricted to HTTP/1.1, with no `Server` header and limits tighter than
  Kestrel's defaults (10 s to receive headers, 16 KB header block, 64 headers).
- A single terminal request handler instead of ASP.NET Core endpoint routing.
- Forwarding of every request to one configured upstream (`Gateway:Upstream`), through one
  long-lived `SocketsHttpHandler` connection pool. There is no routing yet.
- Request and response bodies are streamed in both directions, never buffered. A client that
  disconnects cancels the upstream request.
- Header policy ([ADR 0002](docs/adr/0002-header-forwarding-policy.md)): hop-by-hop and
  `Connection`-nominated headers are dropped, `Host` is rewritten, and client-supplied
  `X-Forwarded-*`/`Forwarded` headers are replaced with values the gateway sets itself.
- The gateway's own errors are JSON with a stable code, so they are distinguishable from upstream
  responses: `404 no_route` (no upstream configured), `502 upstream_unavailable`,
  `504 upstream_timeout`, `400 bad_request` (unreadable client body).
- A demo upstream service that echoes what it received, with `?delay_ms=N` and `?status=CODE`
  to simulate slow or failing backends, and `?chunks=N&chunk_delay_ms=M` to stream a response.
- Integration tests that run both apps on real loopback sockets on ephemeral ports.

## Layout

```
src/GateSharp/                 the gateway
tools/GateSharp.DemoService/   fake upstream used for local runs and tests
tests/GateSharp.Tests/         xUnit v3 tests (Microsoft.Testing.Platform)
docs/adr/                      architecture decision records
Directory.Build.props          shared compiler settings (nullable, warnings-as-errors, analyzers)
Directory.Packages.props       central NuGet versions
global.json                    SDK pin and test runner selection
```

## Running it

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test

# terminal 1: a demo upstream
dotnet run --project tools/GateSharp.DemoService -- --name users-1 --urls http://127.0.0.1:9001
# terminal 2: the gateway on :8080, forwarding to Gateway:Upstream from appsettings.json
dotnet run --project src/GateSharp

curl -s localhost:8080/users/42 -H 'X-Forwarded-For: 6.6.6.6'   # echo shows the gateway's XFF, not yours
curl -i 'localhost:8080/users/42?status=503'                     # upstream's 503 passes through
curl -N 'localhost:8080/events?chunks=5&chunk_delay_ms=500'      # lines arrive one by one
# stop the demo service, then:
curl -i localhost:8080/users/42                                  # 502 upstream_unavailable
```

## License

MIT
