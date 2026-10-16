# GateSharp

An API gateway and reverse proxy written in C# on .NET 10 and Kestrel. Routing, forwarding,
connection management and resilience are implemented by hand (no YARP). The project exists to
understand and measure the design choices behind production gateways, not to replace them. It
is the .NET sibling of [RustGate](https://github.com/KamsGitHub/rustgate).

> **Status: Milestone 1 (foundation).** This README only describes behaviour that is implemented,
> and will only quote benchmark numbers produced by scripts in this repo.

## What works today

- A Kestrel host restricted to HTTP/1.1, with no `Server` header and limits tighter than
  Kestrel's defaults (10 s to receive headers, 16 KB header block, 64 headers).
- A single terminal request handler instead of ASP.NET Core endpoint routing. No routes exist
  yet, so every request gets a stable gateway error: `404 {"error":"no_route",...}`.
- A demo upstream service that echoes what it received, with `?delay_ms=N` and `?status=CODE`
  to simulate slow or failing backends.
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
# terminal 2: the gateway on :8080
dotnet run --project src/GateSharp

curl -i localhost:8080/users/42                 # 404 no_route (no routing until Milestone 2-3)
curl -s 'localhost:9001/users/42?status=503'    # demo service directly
```

## License

MIT
