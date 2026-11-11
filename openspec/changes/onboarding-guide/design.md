## Context

The motivation is in proposal.md. What exists now:

- **`web/src/views/installCli.ts`** is the Tokens page's "Install the CLI" panel from cli-install §5. It fetches
  `/api/cli`, has OS tabs, a Copy button with a status line, and a no-builds note that shows the server's Problem
  `detail`.
- **`tokens.ts`** already fetches `GET /api/tokens`, which returns only your own active tokens, each with `lastUsedAt`.
  That field is null until the token has reported.
- **The Project page** (`projects.ts`, `linkBlock`) shows a "Link a repo" block: the `.panopticon.json` and a Copy
  button.
- **The server** maps the CLI routes with `MapGet`. The SPA fallback (`MapFallbackToFile`, which excludes only
  `api`/`mcp`) takes any method, so a HEAD on `/cli/...` gets `index.html`. `TypedResults.PhysicalFile` is called
  without `lastModified` or `entityTag`, so it sends neither header. That's the missing ETag seen in production; a proxy
  isn't stripping it.

## Goals / Non-Goals

**Goals:**

- One guide component, used on both pages.
- Step state from data the page already has.
- No new endpoint.

**Non-Goals:**

- Live step updates. The state is read when the page loads, or reloads its tokens; the guide doesn't subscribe to the
  event stream.
- Per-agent guides beyond Claude Code and a generic MCP stdio snippet.
- A different marketplace for self-hosted forks.
- Linking a repo inside the guide.

## Decisions

### D1. `setupGuide.ts` replaces `installCli.ts`

`setupGuide(env, { tokens })` renders the three steps. The install step keeps `installCli.ts`'s rendering and
behaviour (tabs, copy, no-builds note) and moves into the new module, so no second install component is left behind.
The guide takes the token list from its host page:

- the Tokens page passes the list it already loads, and re-renders the guide after a revoke;
- the Projects page fetches `/api/tokens` alongside `/api/projects`.

*Alternative:* the guide fetches its own tokens. On the Tokens page that would mean a second, duplicate request.

### D2. The log-in step's server URL comes from the manifest

`panopticon login --server <url>` uses the server's public URL. The source is the origin of the manifest's
`installSh`, which the server builds on `Panopticon:PublicUrl` (cli-distribution). With no CLI builds (a 404) it falls
back to `location.origin`.

- *Why:* the command matches the install script's own "Next, log this machine in" line exactly, even behind a proxy
  whose host differs from the public URL.
- *Alternative:* a new `/api/server` endpoint. Not worth adding for a single string.

### D3. Done-ness is computed in the browser

`done = { install: tokens.length > 0, login: tokens.length > 0, connect: tokens.some(t => t.lastUsedAt) }`. Install
can't be observed separately, but a login implies an installed CLI. A done step gets a check mark and a muted
"Done" label, and keeps its commands (spec: "Steps marked done"). It isn't collapsed, because setting up a second
machine is a normal thing to do.

### D4. Hiding on the Projects page is per browser

Pressing "Hide" sets `panopticon.setupGuide.hidden = "1"` in `localStorage`, wrapped in try/catch (`testShell` already
covers storage that throws). The guide shows on Projects only when no token has reported **and** the key isn't set.
There's no "show again" control on Projects, because the Tokens page always has the guide.

*Alternative:* a server-side preference. That needs a new column and endpoint for a convenience flag.

### D5. The connect step's commands are constants, and so is the snippet

The three plugin commands and the `.mcp.json` snippet are constants in `setupGuide.ts`, and the tests pin each one.
The snippet is the same JSON as `docs/cli.md`'s Claude Code section and the plugin's own `.mcp.json`. The other-agents
option is a disclosure (`<details>`), so the plugin stays the headline.

### D6. GET and HEAD on the CLI routes

The four CLI routes use `MapMethods(path, ["GET", "HEAD"], …)`. Kestrel sends headers and no body for HEAD, and the
route now matches HEAD, so the fallback never sees it. An unknown `/cli/<platform>/<file>` still reaches the
endpoint, which answers 404. A test pins HEAD on a build, an unknown build, `/install.sh` and `/api/cli`.

*Alternative:* adding `cli` to the fallback's exclusions. That makes a HEAD on an unknown path a bare 404, but it still
answers HEAD on a known build with no headers. Mapping HEAD fixes both.

### D7. ETag from the SHA-256, Last-Modified from the file

`CliBuild` gains `LastModified`, the file's last-write time in UTC, read when the catalogue loads. `Download` passes
`entityTag: new EntityTagHeaderValue($"\"{sha256}\"")` and `lastModified` to `PhysicalFile`. That result already
answers a matching `If-None-Match` with 304, and keeps the `Cache-Control: no-cache` from 9.2.

- *Why the SHA-256:* the bytes and the ETag can't drift, and a new release always gets a new ETag, even if the
  timestamps were preserved.

### D8. Project page wording

Under the link file's Copy row, one muted line: "Or ask your agent to link this repo to <CODE>: it can look the
Project up and write the file." That's all there is to the projects delta.

### D9. Look and feel

The guide follows `web/DESIGN.md`'s quiet tool voice: numbered steps, `--ink` for the pressed OS tab, no `--lit`. The
step numbers become check marks when done. It goes through the `frontend-design` pass, and `web/DESIGN.md` gains a
"Setup guide" section, replacing the "Tokens: Install the CLI" bullet. Emmz reviews it in a real browser before it
lands.

## Risks / Trade-offs

- [The marketplace or plugin is renamed, or Claude Code's `/plugin` syntax changes] → The commands are in one place
  (D5) and pinned by tests. `docs/cli.md` gives the same commands, so a rename is a two-file edit.
- [A member who never reports (a viewer) still sees the guide on Projects] → The Hide control (D4).
- [The step state lags a report until the page reloads] → An accepted non-goal. The dashboard shows the report itself
  live.
- [A token was revoked after it reported] → Revoked tokens aren't listed, so connect can go back to not done. That's
  correct: that machine no longer reports.
