# Proposal

## Why

Emmz is starting to run Claude on cloud VMs and has no single overview of what her agents are doing
across projects (`docs/DISCOVERY.md`). The architecture's riskiest idea is the CLI acting as a local MCP
bridge in front of an online MCP endpoint (ADR-0001), so the first change proves the whole path end to
end, thinly, before auth, history, filtering and metrics are built on it.

## What Changes

- New repo scaffolding:
  - a .NET 10 solution with server, CLI and shared contracts projects and their tests;
  - a vanilla TypeScript + Vite web app;
  - pinned SDK, formatting and analyzer settings;
  - a multi-stage Dockerfile and a local compose file for Postgres and S3-compatible storage.
- **Server:** an online MCP endpoint with a `report_progress` tool. It records which agent is working on
  which change in which repo, stores the reported `tasks.md`, and works out section and overall progress
  from it.
- **Server:** a dashboard API and live event stream, plus a page showing **one card per change** with
  its repo, change name, agent(s), a current-section progress bar and an overall progress bar. Cards
  update live, with no refresh. The look is designed with the `frontend-design` skill ("fun but clean").
- **CLI:** `panopticon mcp`, a stdio MCP server that agents configure. Its `report_progress` tool:
  - reads the repo identity from the local git remote;
  - reads `openspec/changes/<change>/tasks.md`;
  - forwards both to the server.
- **Temporary auth:** a single bootstrap bearer token, set in server config and passed to the CLI,
  protects the MCP endpoint. Per-user tokens and sign-in replace it in a later change.

Not in this change: magic-link sign-in, device login, per-user tokens, drill-down, the full document set,
archive → history, filtering, idle/stale state, metrics, `panopticon setup`, GitHub Releases, Railway
deployment. Until sign-in lands, the dashboard is unauthenticated, so this change is not deployed to the
internet.

## Capabilities

### New Capabilities

- `progress-reporting`: the server's online MCP endpoint. It accepts authenticated progress reports for
  an OpenSpec change (repo identity, change, current section, reporting agent, `tasks.md`) and keeps the
  latest state per change.
- `task-progress`: how a change's `tasks.md` becomes per-section and overall progress (sections,
  checkbox counting, choosing the current section).
- `live-dashboard`: the web dashboard's live view, with one card per change showing repo, change,
  agents and two progress bars, updating without a refresh.
- `cli-bridge`: the CLI's local stdio MCP server, which resolves the repo identity and reads change
  documents from the local working copy, then forwards reports to the server.

### Modified Capabilities

_None. There are no existing specs._

## Impact

- **New code:** `Panopticon.slnx`, `src/Panopticon.Server`, `src/Panopticon.Cli`,
  `src/Panopticon.Contracts`, `tests/*`, `web/`, `Dockerfile`, `compose.yaml`, `global.json`,
  `.editorconfig`, `Directory.Build.props`.
- **New dependencies:**
  - NuGet: `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, EF Core 10 + Npgsql,
    `AWSSDK.S3`, Markdig, xUnit v3, Testcontainers;
  - npm: Vite, TypeScript, Vitest, ESLint, Prettier.
- **Machine dependencies** are in `docs/dependencies.md`. Docker is needed for the test gate.
- **Decisions** are bound by `docs/adrs/ADR-0001` to `ADR-0005`.
