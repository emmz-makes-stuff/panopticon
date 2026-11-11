# ADR-0002: .NET 10 for server and CLI, vanilla TypeScript + Vite for the web UI

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** Emmz

## Context

There are three components: an internet-facing server with an MCP endpoint and a live dashboard, a CLI
installed on cloud VMs that is also a stdio MCP server (ADR-0001), and a web UI that looks "fun but
clean" (designed with the `frontend-design` skill). Setup should take minutes on a new VM.

## Decision

- **Server:** ASP.NET Core on **.NET 10 (LTS)**, minimal APIs, using the official **MCP C# SDK**
  (`ModelContextProtocol.AspNetCore`, streamable HTTP, stateless). Live updates use .NET 10's
  `TypedResults.ServerSentEvents`.
- **CLI:** a .NET 10 console app published with **Native AOT**, one self-contained binary per OS/arch,
  using `ModelContextProtocol` (stdio server + `HttpClientTransport` client). Tools are registered with
  the AOT-safe generic `WithTools<T>()`, never `WithToolsFromAssembly`.
- **Contracts:** a shared .NET library that both the server and the CLI reference.
- **Web:** **vanilla TypeScript + CSS**, built with **Vite**, with no UI framework. Live updates arrive
  through the browser's `EventSource`.
- The SDK is pinned through `global.json` to 10.0.404 with `rollForward: latestFeature`. (Raised from 10.0.400, whose bundled AOT compiler had a known vulnerability, then from 10.0.403 for the 2026-11-10 servicing release: ASP.NET Core 10.0.15 fixes CVE-2026-114149.)

## Evidence

- The MCP C# SDK (v2.3) documents streamable-HTTP hosting with `MapMcp`, recommends stateless mode,
  supports bearer auth via `AddAuthorizationFilters()`, and ships an AOT compatibility test app.
- ASP.NET Core 10 "What's new" documents `TypedResults.ServerSentEvents`.

## Consequences

- One language for the server and CLI, with shared types for the MCP tool contracts.
- VMs need no .NET runtime, just a downloaded binary.
- AOT constrains CLI dependencies (no reflection-based serialization; use `System.Text.Json` source
  generation). An AOT publish runs as a gate so a breaking dependency is caught early.

## Alternatives rejected

- **TypeScript everywhere (Node server + CLI).** Every VM would need Node, or a less mature
  single-binary bundler.
- **Go server + CLI.** Static binaries are trivial, but it isn't the house stack, and its MCP SDK is
  younger.
