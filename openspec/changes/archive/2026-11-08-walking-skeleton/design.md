# Design

## Context

The repo is greenfield. It holds only `openspec/` and `docs/` (DISCOVERY, ADR-0001 to ADR-0005,
dependencies). The motivation is in proposal.md, the behaviour in `specs/`. On this machine:

- several .NET SDKs are installed (8, 9, 10.0.302, 10.0.400, 11 RC), so the pin has to be explicit;
- Node 24, Docker via OrbStack, `gh` and `railway` are installed.

## Goals / Non-Goals

**Goals:**

- Prove the ADR-0001 path end to end:
  agent → `panopticon mcp` → online MCP endpoint → Postgres + S3 → SSE → live card.
- Have every gate (`dotnet` and `web` stacks) green from the first section onwards. That includes the
  AOT publish, so CLI dependencies stay AOT-safe from day one.
- Shape storage so later changes (history, metrics, full documents) add to it rather than rewrite it.

**Non-Goals:**

- Multi-instance live fan-out. One server instance is assumed in this change.
- Any production deployment, CI or release pipeline.
- Polishing every UI state beyond what the specs require. The `frontend-design` pass covers the card and
  dashboard shell only.

## Decisions

### Binding decisions from the ADRs

- **D1. CLI is a local MCP bridge (ADR-0001).** The server's online MCP endpoint is the only server
  API for reporting. `panopticon mcp` is a stdio MCP server for agents and an MCP *client* of the server.
  Rejected: agents sending docs as tool arguments; agents reporting directly and syncing separately.
- **D2. .NET 10 + vanilla TypeScript (ADR-0002).**
  - Server: ASP.NET Core minimal APIs with `ModelContextProtocol.AspNetCore` (streamable HTTP,
    `HttpServerSessionMode.Stateless`).
  - CLI: Native AOT with `ModelContextProtocol` (stdio server + `HttpClientTransport` client), registering
    tools **only** through generic `WithTools<T>()`, with `System.Text.Json` source generation for every
    serialised type.
  - Web: vanilla TS + CSS built with Vite.
  - Rejected: TypeScript everywhere; Go.
- **D3. PostgreSQL + S3-compatible storage (ADR-0003).** Structured data lives in Postgres, documents in
  any S3-compatible store (Railway Bucket by default). Rejected: bucket only; SQLite on a volume.
- **D4. One container on Railway, CLI on GitHub Releases (ADR-0004).** This change delivers the
  Dockerfile only. Deployment and releases come later. Rejected: ECS Fargate; Azure Container Apps.
- **D5. Magic-link sign-in via Resend, and per-user API tokens through a device-style CLI login
  (ADR-0005).** Both come in a later change. This change uses a **single bootstrap bearer token**
  (`Panopticon:BootstrapToken`) as a stand-in. It's compared in constant time
  (`CryptographicOperations.FixedTimeEquals`), and a missing value fails startup. Rejected for the
  skeleton: building real auth first, which would delay proving D1.

### Supporting decisions

- **D6. Data access: EF Core 10 + Npgsql, code-first migrations.** Rejected: Dapper, because migrations
  and the growing model (history, metrics) favour EF. The skeleton's tables:
  - `repos` (host, owner, name; unique on the lower-cased triple);
  - `changes` (repo, name, unique per repo; latest progress snapshot as `jsonb`; `last_reported_at`);
  - `agents` (client name, machine name; unique pair);
  - `change_agents` (change, agent, `last_reported_at`);
  - `report_events` (change, agent, reported section, `tasks_sha256`, `received_at`).
  Case-insensitive identity is done by storing lower-cased keys alongside the display values. Migrations
  run at startup in Development and through an explicit `--migrate` flag otherwise.
- **D7. Object storage: `AWSSDK.S3` with configurable `ServiceURL` and `ForcePathStyle`.** Documents are
  **content-addressed**: `documents/sha256/<hex>`, written only if absent, so identical content is
  stored once (spec: Document retention). Report events reference the hash. Later history and drill-down
  read these objects. Rejected: a per-change "latest" key, which loses history.
- **D8. Markdown: Markdig on the server, raw HTML disabled.** A single `TasksParser` works on the
  Markdig AST (headings level 2, list items whose inline text starts with `[ ]`/`[x]`). It implements the
  `task-progress` rules, including OpenSpec's "only `x` counts" rule, which is broader than Markdig's
  task-list extension. That's why the checkbox is matched on the paragraph's leading text, not via
  `UseTaskLists()`. Rendering for drill-down later uses the same pipeline with `DisableHtml()`.
  Rejected: client-side `marked` + DOMPurify, which would mean two parsers that could disagree.
- **D9. Live updates: SSE via `TypedResults.ServerSentEvents` + browser `EventSource`.**
  - An in-process `ChangeBroadcaster` uses one bounded `Channel` per subscriber, and slow subscribers
    drop their oldest events.
  - Events: `change-updated` carries the full card DTO.
  - On (re)connect the client first fetches `GET /api/changes`, then applies events. Event ids let it
    ignore stale updates by comparing `lastReportedAt`.
  - Rejected: SignalR, which adds a client library and is more than one-way updates need.
- **D10. Web delivery: Vite builds into `src/Panopticon.Server/wwwroot`.** That folder is git-ignored
  and served with `UseStaticFiles` + `MapFallbackToFile("index.html")`. In development, Vite's dev
  server proxies `/api` and `/mcp` to the server.
- **D11. Charts: hand-built SVG.** None in this change. Recorded for the metrics change.
- **D12. Tests: xUnit v3 on Microsoft.Testing.Platform.** `global.json` sets
  `"test": { "runner": "Microsoft.Testing.Platform" }`. Rejected: MSTest, TUnit.
- **D13. Integration tests: Testcontainers (PostgreSQL, SeaweedFS S3 — MinIO no longer publishes images)** with `WebApplicationFactory`, plus an
  in-process MCP client calling the real endpoint. Rejected: fakes only.
- **D14. GitHub hosts the repo and Releases.**
- **D15. Shared contracts.** `Panopticon.Contracts` holds the server tool's input/output records and a
  `JsonSerializerContext`, so the server and the AOT CLI agree on the wire format.
- **D16. MCP endpoint path `/mcp`.** The read API is under `/api` and the UI at `/`.
- **D17. Local development** uses `compose.yaml` with Postgres 17 and SeaweedFS (S3 gateway, credentials from `local/seaweedfs-s3.json`), plus an `amazon/aws-cli` bucket-init
  container.
- **D18. SDK pin.** `global.json` pins `10.0.403` with `rollForward: latestFeature` (10.0.400 bundled a vulnerable AOT compiler, GHSA-p3jp-9v83-76w9; see devlog). All projects target
  `net10.0`. `Directory.Build.props` sets `Nullable`, `TreatWarningsAsErrors`,
  `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild=true`. `.editorconfig` holds the
  formatting rules.

### Gate commands

Stack names: `dotnet`, `web`. Every command runs from the repo root, non-interactively, and exits
non-zero on failure.

- dotnet build: `dotnet build Panopticon.slnx -c Release -warnaserror`
- dotnet test: `dotnet test --solution Panopticon.slnx -c Release`
- dotnet format: `dotnet format Panopticon.slnx --verify-no-changes`
- dotnet aot: `dotnet publish src/Panopticon.Cli/Panopticon.Cli.csproj -c Release --use-current-runtime -warnaserror -o artifacts/cli`
- web install (prerequisite): `npm --prefix web ci`
- web build: `npm --prefix web run build`
- web test: `npm --prefix web run test`
- web format: `npm --prefix web run format:check`
- web lint: `npm --prefix web run lint`

Notes:

- There's no separate dotnet lint gate, because analyzers run in the build through `AnalysisLevel` and
  `EnforceCodeStyleInBuild`.
- The dotnet test gate needs Docker (Testcontainers).
- The web scripts in `web/package.json`:
  - `build` = `tsc --noEmit && vite build`
  - `test` = `vitest run`
  - `format:check` = `prettier --check .`
  - `lint` = `eslint .`

## Risks / Trade-offs

- **[Unauthenticated dashboard]** Anyone who can reach it can read it. → This change is not deployed.
  The spec says so explicitly, and sign-in is the next change.
- **[In-process broadcaster]** A second instance wouldn't see the first one's events. → One instance in
  this change. Later options are Postgres `LISTEN/NOTIFY` (preferred, no new service) or Redis.
- **[AOT + MCP SDK reflection]** A missed `[JsonSerializable]` type fails at runtime, not at compile
  time. → The AOT gate runs `-warnaserror`, and an integration test drives the *published* CLI binary
  over stdio.
- **[Markdig and OpenSpec checkbox rules differ]** → The parser matches leading text itself (D8), and
  the spec scenarios become unit tests.
- **[Agent client name is self-reported]** Agents can name themselves anything. → That's acceptable,
  because it's display-only. Per-user tokens later tie reports to a person.
- **[Testcontainers in the sandbox]** The Docker socket isn't reachable from the sandboxed shell. → The
  test gate runs unsandboxed on this machine (a known quirk). CI has Docker.
- **[Large tasks.md]** → A 1 MiB limit at the tool boundary (spec), plus Kestrel request-size limits.

## Migration Plan

Greenfield, so there's nothing to migrate or roll back. The first schema migration creates the tables in
D6.

## Open Questions

- Should the empty-state copy point at a docs page or inline instructions? That's decided during the
  `frontend-design` pass. Either way it shows the env vars and the `panopticon mcp` command.
