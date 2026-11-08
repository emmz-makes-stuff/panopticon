# Stopped: §6.5 needs a real Claude Code run of `panopticon mcp`

<!-- Section: 6 · Tasks: 6.1–6.5 · Base: 0bffe20 · Status: stopped, §6 uncommitted on purpose -->

**Why stopped:** CLAUDE.md's "Stop and ask" says a real agent such as Claude Code driving `panopticon mcp`
end to end needs human verification. Everything else in §6 is built, and the gates are green. 6.1–6.4 are
ticked, 6.5 isn't, and **all of §6 is uncommitted** (see `git status`).

**Question for Emmz:** does the `docs/cli.md` `.mcp.json` snippet work in a real Claude Code session? Recipe below.

## What landed (uncommitted)

- **6.1 `Git/GitRemote.Parse`** (CLI) handles `ssh://`, `https://`, `http://`, `git://` and scp-style remotes.
  - It drops credentials (`Uri.Host` / text after `@`). Its errors never echo the URL.
  - `_git` segments are dropped anywhere. **`v3` is dropped only as the first segment on `ssh.dev.azure.com`**
    (which also maps to `dev.azure.com`), so a GitLab group called `v3` survives. That's a decision: the spec
    says "Azure DevOps markers", and dropping `v3` everywhere would be wrong. `visualstudio.com` legacy hosts
    aren't special-cased.
  - Local paths, `file://` and remotes with fewer than two path segments are rejected.
- **6.2:**
  - `Git/GitRepository` runs `git rev-parse --show-toplevel` and `git remote get-url origin` as processes,
    with `GIT_TERMINAL_PROMPT=0` and stdin closed. git is already in `docs/dependencies.md`.
  - `ChangeDocuments` accepts a single plain folder name only (no `/ \ :`, not `.`/`..`, no surrounding
    spaces, no invalid filename chars), and also checks the combined path's parent.
  - A missing change folder errors with the folder path. A folder without `tasks.md` errors with the file
    path.
- **6.3 `Bridge/`:**
  - `BridgeSettings` reads the env vars. `PANOPTICON_SERVER_URL` is a base URL, and `/mcp` is appended
    unless it's already there. Displayed URLs drop user-info, query and fragment.
  - `ServerForwarder` makes **one short-lived `McpClient` per report** (the server is stateless, and this
    survives server restarts). It uses a 30 s HTTP timeout. It maps failures as follows:
    - 401/403 → "rejected the token".
    - Other status codes → the status.
    - Connection failure → "Couldn't reach … at <url>".
    - Timeout.
    - A server `IsError` → "The Panopticon server refused the report: …".
  - `ProgressReporter` checks config first, then root, origin, tasks and agent (MCP `ClientInfo.Name`, or
    `unknown-client`, plus `Environment.MachineName`).
  - `BridgeTools` returns tool errors (`IsError`) for every `BridgeException`.
  - `BridgeHost` uses `Host.CreateEmptyApplicationBuilder`. **Logging goes through a small custom
    `TextWriterLoggerProvider` to stderr** (no console-logger options binding). The minimum level is
    Warning, and `Panopticon.*` logs at Information. `WithTools<BridgeTools>(options)` resolves through a
    local `BridgeJsonContext` (`string`, `int?`) combined with `McpJsonUtilities.DefaultOptions`.
  - `CliApp.RunAsync` (async now), and `mcp` runs the host with `WithStdioServerTransport()`.
  - Package: added `Microsoft.Extensions.Hosting` **10.0.14** to `Directory.Packages.props`. Contrary to
    the §5 note, it was *not* already pinned. That's a NuGet package, not a machine dependency, so
    `docs/dependencies.md` is unchanged.
- **Test-project choice (asked for in the §5 note):** a new **`tests/Panopticon.Cli.IntegrationTests`**.
  - It references `Panopticon.Cli`, `Panopticon.Server.Tests` (for `PanopticonFactory` and the fixtures,
    with its own `[assembly: AssemblyFixture]` lines) and `Panopticon.Cli.Tests` (for `TempGitRepo`), plus
    `Microsoft.AspNetCore.Mvc.Testing` directly.
  - This keeps `Panopticon.Cli.Tests` Docker-free.
  - `BridgeHarness` runs the real `BridgeHost` over `System.IO.Pipelines` pipes with a named MCP client.
- **6.4:**
  - `make aot` is clean with `-warnaserror`.
  - `PublishedCliTests` launches `artifacts/cli/panopticon mcp` over stdio against the server on **real
    Kestrel** (`WebApplicationFactory.UseKestrel(0)` + `StartServer()`, new in .NET 10). It lists tools,
    reports, and checks the card's repo, agent client and machine.
  - A raw-process test checks that every stdout line is JSON while the bridge logs a warning, that stderr
    names the missing variable, and that the bridge **exits 0 on stdin EOF**.
  - **Makefile: `test: aot`**, so `make test` publishes first. `make gates` runs aot once. CLAUDE.md's
    `make test` line now says so. A missing binary fails the test loudly, and `PANOPTICON_CLI_PATH`
    overrides the path.
- **6.5:** `docs/cli.md` covers install via `make aot`, the env vars, the `.mcp.json` snippet (with
  `${PANOPTICON_TOKEN}` expansion and a `${PANOPTICON_SERVER_URL:-http://localhost:7480}` default), the
  `claude mcp add` alternative, other agents, and troubleshooting.

## Checks done

- **Mechanical check of the documented setup:**
  - Ran compose and the dev server on 7480, then the published binary from `src/` with the snippet's env,
    over raw JSON-RPC.
  - `initialize` gave serverInfo `panopticon`. `tools/list` gave `["report_progress"]`.
  - `report_progress {change: walking-skeleton, section: 6}` gave `Overall 18/30 · section 6 CLI bridge 0/5`,
    with structured content.
  - stderr held only the info log line.
  - The server was stopped afterwards (PIDs checked: `dotnet run` and `Panopticon.Server`). Compose may
    still be up.
- **Gates:** `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` (120 tests) `FORMAT_EXIT:0` `WEB_INSTALL_EXIT:0`
  `WEB_BUILD_EXIT:0` `WEB_TEST_EXIT:0` `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0` `VALIDATE_EXIT:0` `GATES_EXIT:0`.

## Deferred / gotchas

- **The "Runs on a clean VM" scenario (Linux binary without .NET) is not verified here.** Native AOT can't
  cross-compile macOS → Linux. It belongs with the releases/CI change (ADR-0004). The macOS binary is
  self-contained and is what the e2e test drives.
- **CA1873 fires on any computed `[LoggerMessage]` argument, even inside an `IsEnabled` guard.** The
  reporter logs `Done`/`Total`/`Section` as plain values.
- **xUnit `Assert.False(bool?)` fails on `null`**, and `CallToolResult.IsError` is `null` on success. Use
  `Assert.True(result.IsError is not true, …)`.
- The integration tests log the server's `TasksDocumentTooLargeException` as `fail:` during the
  oversize-tasks test. That's expected.

## Recipe (for Emmz)

In the repo root:

```sh
docker compose up -d --wait
make aot
# terminal 2:
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server -c Release --no-launch-profile
```

Create `.mcp.json` in the repo root from `docs/cli.md`, with `"command"` set to the absolute path of
`artifacts/cli/panopticon` (or put that folder on `PATH`). Then:

```sh
export PANOPTICON_TOKEN=dev-bootstrap-token
claude
```

In Claude Code:

1. Approve the `panopticon` project MCP server. `/mcp` should show it connected, with one tool.
2. Ask: *"Call the panopticon report_progress tool for change walking-skeleton, section 6."*
3. Expect a reply like `Overall 22/30 · section 6 CLI bridge 4/5`.
4. Open `http://localhost:7480/api/changes`. The `walking-skeleton` card should list agent `claude-code`
   on this Mac's machine name.

Afterwards, delete `.mcp.json` (or say whether it should be committed), and stop the server.

## Next

- **On PASS:** tick 6.5, write the §6 section-done note (fold this note's content in), and make the §6
  commit `feat(walking-skeleton): CLI bridge (§6)` with lines 6.1–6.5. Then run the context check and start
  **§7, task 7.1** (the `frontend-design` skill, then `web/DESIGN.md`). 7.1 is itself a human-verification
  point (look and feel).
- **On FAIL:** take the pasted Claude Code output or error, fix it within §6, rerun `make gates`, and
  re-verify.
