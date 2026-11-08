# Tasks

## 1. Repo scaffolding and gates

- [x] 1.1 `git init`, add `.gitignore` (bin/obj, `artifacts/`, `web/node_modules`, `src/Panopticon.Server/wwwroot`) and `.editorconfig`, and verify `git status` shows only the intended files
- [x] 1.2 Add `global.json` (SDK `10.0.403`, `rollForward: latestFeature`, test runner `Microsoft.Testing.Platform`) and `Directory.Build.props` (`net10.0`, Nullable, TreatWarningsAsErrors, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`), and verify `dotnet --version` in the repo reports 10.0.4xx
- [x] 1.3 Create `Panopticon.slnx` with `src/Panopticon.Server`, `src/Panopticon.Cli` (`PublishAot=true`), `src/Panopticon.Contracts`, and xUnit v3 test projects `tests/Panopticon.Server.Tests`, `tests/Panopticon.Cli.Tests`, each with one trivial passing test, and verify the build, test, format and aot gates from design.md all exit 0
- [x] 1.4 Create `web/` (Vite + TypeScript, Vitest + happy-dom, ESLint, Prettier) with the `build`/`test`/`format:check`/`lint` scripts from design.md and Vite `outDir` set to `../src/Panopticon.Server/wwwroot`, and verify `npm --prefix web ci` and the four web gates exit 0
- [x] 1.5 Add `compose.yaml` (Postgres 17, SeaweedFS S3, and an aws-cli bucket-init container) and verify `docker compose config` passes and `docker compose up -d` brings both services healthy

## 2. Task progress parsing

- [x] 2.1 Implement the `tasks.md` parser in the server (Markdig AST: level-2 sections with number/title, checkbox detection per the `task-progress` rules, overall and per-section counts), and verify unit tests cover every Sections, Task counting and Overall progress scenario
- [x] 2.2 Implement current-section selection (reported section if it exists, else first unfinished, else last section with tasks), and verify unit tests cover all four Current section scenarios
- [x] 2.3 Enforce the 1 MiB limit and make sure no raw HTML survives parsing, and verify the Untrusted content scenarios as unit tests

## 3. Persistence

- [x] 3.1 Add the EF Core 10 + Npgsql model from design D6 (repos, changes, agents, change_agents, report_events, with lower-cased identity keys) and the initial migration, and verify `dotnet ef migrations` builds and a Testcontainers Postgres test applies it
- [x] 3.2 Implement the content-addressed S3 document store (`documents/sha256/<hex>`, write-if-absent, configurable `ServiceURL`/`ForcePathStyle`), and verify a Testcontainers SeaweedFS test shows identical content stored once
- [x] 3.3 Implement the report-recording service (upsert repo/change/agent, update change snapshot and change_agent time, append report_event), and verify integration tests for "Repeat report updates the same change", "Case-insensitive identity", "Two agents on one change" and "Report kept as an event"

## 4. Online MCP endpoint

- [x] 4.1 Add shared contract records and a `JsonSerializerContext` in `Panopticon.Contracts` for the `report_progress` input/output, and verify a round-trip serialisation unit test
- [x] 4.2 Add bootstrap bearer auth (constant-time compare, startup failure when unset) protecting `/mcp`, and verify integration tests for the Missing token, Wrong token and No token configured scenarios
- [x] 4.3 Map the MCP endpoint at `/mcp` (streamable HTTP, stateless) with the `report_progress` tool wired to parsing (section 2) and recording (section 3), including field validation, and verify an in-process MCP client test covers "Tool is discoverable", "Valid report accepted" and "Missing required field"
- [x] 4.4 Add the in-process change broadcaster (bounded per-subscriber channels) and publish on every accepted report, and verify a test that a subscriber receives the updated card after a report

## 5. Dashboard read API and live stream

- [x] 5.1 Add `GET /api/changes` returning card DTOs, most recently reported first, and verify an integration test with two changes in two repos
- [x] 5.2 Add `GET /api/events` using `TypedResults.ServerSentEvents` that streams `change-updated` events from the broadcaster, and verify an integration test reads an SSE event after a report
- [x] 5.3 Serve `wwwroot` static files with an `index.html` fallback, plus Development-time migrations and a `--migrate` flag, and verify `dotnet run` against compose serves the built web app at `/`

## 6. CLI bridge

- [x] 6.1 Implement git-remote parsing into host/owner/repo (SSH, scp-style, HTTPS, Azure DevOps markers, credentials stripped), and verify unit tests for every "Repo identity from git remote" scenario
- [x] 6.2 Implement repo-root discovery and the change-document reader with path-traversal refusal, and verify tests using a temp git repo for the Repo root and "Change documents read locally" scenarios
- [x] 6.3 Implement `panopticon mcp` (stdio server via `WithTools<T>()`, stderr-only logging, env-var config, client-name + host-name agent identity, forwarding through `HttpClientTransport` with the bearer token, server errors mapped to tool errors), and verify tests for the Configuration, Agent identity and "Forwarding to the server" scenarios against an in-process server
- [x] 6.4 Publish the CLI with the aot gate, and verify an end-to-end test that launches the published binary over stdio, lists tools, and reports successfully to a Testcontainers-backed server
- [x] 6.5 Document agent configuration (an MCP config snippet for Claude Code plus the env vars) in `docs/cli.md`, and verify the documented snippet works against a local run

## 7. Web dashboard

- [x] 7.1 Run the `frontend-design` skill to set the "fun but clean" direction (type, colour tokens with light/dark, card and progress-bar treatment, empty state), and record it in `web/DESIGN.md`
- [x] 7.2 Implement the card list from `GET /api/changes` (card content, two labelled bars with counts, empty-bar state, empty dashboard state), and verify Vitest tests for the "One card per change", "Card content" and "Two progress bars" scenarios
- [x] 7.3 Make the bars accessible (`role="progressbar"` with label and `aria-valuenow`, and not colour-only), and verify a Vitest test asserting the accessible name and value
- [x] 7.4 Add live updates via `EventSource` (in-place update, new card at top, auto-reconnect with full refresh and an offline indicator, stale events ignored), and verify Vitest tests with a fake `EventSource` for every "Live updates" scenario

## 8. Container and end-to-end check

- [ ] 8.1 Add a multi-stage `Dockerfile` (node stage builds web into wwwroot, then dotnet publish, then aspnet runtime image), and verify `docker build` succeeds and the container serves `/` and `/mcp` against compose services
- [ ] 8.2 Document local running (compose, env vars including `Panopticon:BootstrapToken`, S3 settings, migrations) in `README.md`, and verify a fresh clone following it reaches a working dashboard
- [ ] 8.3 End-to-end check: with the server running, configure Claude Code to use the published `panopticon mcp`, report on this change, and verify the card appears and both bars move live as tasks are ticked

## Workflow follow-up

- Archive the change after review with `/opsx:archive`.
- Next changes, per the agreed roadmap: auth (magic links, device login, per-user tokens); drill-down +
  full documents + archive→history; filtering + idle/stale; metrics; `panopticon setup` + Releases +
  Railway deploy.
