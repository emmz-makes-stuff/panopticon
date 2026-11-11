## Why

A new member can install the CLI from the Tokens page, but the UI goes no further than "then log it in with
`panopticon login`". Connecting an agent is only in `docs/cli.md`, as a hand-written `.mcp.json` or `claude mcp add`.
The Claude Code plugin (`panopticon@panopticon-skills`, from the public `emmz-makes-stuff/panopticon-skills`
marketplace) isn't mentioned anywhere in the repo, although it registers the bridge, nudges reports and checks the setup
with `/panopticon:setup`. cli-install's release also showed that the CLI downloads answer HEAD with the app shell and
carry no cache validators.

## What Changes

- **A machine setup guide** in the web app, in three steps:
  - Install the CLI: the command for your OS, as the Tokens panel shows it now.
  - Log in: `panopticon login --server <this server>`.
  - Connect your agent: for Claude Code, add the plugin marketplace, install the plugin and run `/panopticon:setup`.
    For other agents, the `panopticon mcp` stdio settings.

  Each step shows as done once the member's own tokens show it: a token means they've logged in, and a token that has
  reported means their agent is connected.
- **Where it shows:**
  - Always on the **Tokens** page, where it replaces the "Install the CLI" panel.
  - On the **Projects** page, until one of the member's tokens has reported. A member can hide it there, and the
    choice is remembered in that browser.
- **The Project page** keeps its "Link a repo" block and adds the other way: ask your agent to link the repo to this
  Project (it uses `list_projects` and writes `.panopticon.json`). The guide stops at step 3 and doesn't cover linking.
- **CLI downloads:** `/cli/<platform>/<file>`, `/install.sh`, `/install.ps1` and `/api/cli` answer HEAD like GET,
  without a body, rather than with the app shell. Each build download sends an ETag made from its SHA-256, and a
  `Last-Modified`, so a revalidation with `If-None-Match` gets a 304.
- **Docs:** `README.md` and `docs/cli.md` lead with the plugin for Claude Code, and keep the `.mcp.json` and
  `claude mcp add` routes as alternatives.

## Capabilities

### New Capabilities

- `machine-setup`: the signed-in guide to setting up a machine to report: its three steps, how each step is marked
  done, and where it appears.

### Modified Capabilities

- `cli-distribution`:
  - "Install hint on the Tokens page" is now the guide's first step, so the Tokens page shows the guide rather than a
    standalone panel.
  - New: CLI endpoints answer HEAD.
  - New: build downloads carry validators.
- `projects`: new, the Project page names asking an agent as a way to link a repo.

## Impact

- **Web:**
  - New: `web/src/views/setupGuide.ts`, built from `installCli.ts`.
  - Changed: `tokens.ts`, `projects.ts`, `styles.css`, `web/DESIGN.md`, and their tests.
  - The look goes through `frontend-design` and Emmz's review.
- **Server:**
  - `Distribution/CliEndpoints.cs`: maps GET and HEAD, and gives `PhysicalFile` its ETag and last-modified time.
  - `ServerSetup.cs`: keeps `cli` and the install scripts out of the fallback, if HEAD mapping alone isn't enough.
  - Tests in `Panopticon.Server.Tests/Distribution`.
- **No new API:** step state comes from `GET /api/tokens` (`lastUsedAt`), which the Tokens page already fetches.
- **Docs:** `README.md`, `docs/cli.md`.
- No new dependencies.
