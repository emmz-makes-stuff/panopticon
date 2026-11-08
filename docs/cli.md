# The `panopticon` CLI

`panopticon login` gives this machine an API token, approved in the dashboard. `panopticon mcp` is a local
MCP server that agents talk to over stdio. It offers one tool,
`report_progress`, and forwards each report to the Panopticon server's online MCP endpoint (`/mcp`).

The agent only names the change. The bridge supplies the rest from the local working copy:

- **The repo identity**, from the `origin` remote. Any host works (GitHub, GitLab subgroups, Azure DevOps over
  HTTPS or SSH). Credentials in the URL are dropped and never sent or logged.
- **The change's documents**: the current `openspec/changes/<change>/tasks.md` from the repo root, which is
  the git top-level of the bridge's working directory.
- **The agent**: the MCP client name the agent gave when it connected (for example `claude-code`), plus this
  machine's name.

## Install

The CLI is one native executable with no .NET runtime needed. Until releases are published, build it from
this repo with `make aot`. That writes `artifacts/cli/panopticon`. Put it on your `PATH`, or use its full
path as the `command` below.

## Log in

Each machine that reports gets its own API token, for you, through a login you approve in the dashboard:

```sh
panopticon login --server https://panopticon.example.com
```

The CLI prints a code such as `BCDF-GHJK` and a URL like
`https://panopticon.example.com/device?code=BCDF-GHJK`. It doesn't open a browser, because a VM usually has
none. Open the URL wherever you're signed in to the dashboard, check that it shows this machine's name, and
approve it. The CLI then saves the server and token, and says who it's logged in as.

- The code expires after 10 minutes. If nobody approves it by then, the CLI says so and exits 1. Run
  `panopticon login` again.
- If you deny the code, the CLI says the login was denied, and exits 1. No token is issued.
- Ctrl+C stops waiting, with exit code 130.
- Logging in again replaces the saved server and token.
- Without `--server`, the CLI uses `PANOPTICON_SERVER_URL`, then the server it saved last time.

You can see and revoke each machine's token on the dashboard's Tokens page. A revoked token is refused at once.

## Configuration

The login is saved as `{"serverUrl": …, "token": …}` in:

| OS | File |
| --- | --- |
| Linux, macOS | `$XDG_CONFIG_HOME/panopticon/config.json`, or `~/.config/panopticon/config.json` when `XDG_CONFIG_HOME` is unset |
| Windows | `%APPDATA%\panopticon\config.json` |

On Linux and macOS the file is readable by you only (mode 0600, in a 0700 directory). `panopticon --help`
prints its path.

Environment variables override the saved login, value by value:

| Variable | Meaning |
| --- | --- |
| `PANOPTICON_SERVER_URL` | The server's base URL, e.g. `https://panopticon.example.com`. A URL already ending in `/mcp` is used as-is. |
| `PANOPTICON_TOKEN` | The API token for the server. |

The saved token belongs to the saved server. If `PANOPTICON_SERVER_URL` names a different server and
`PANOPTICON_TOKEN` is unset, the bridge doesn't send the saved token there. Each report fails instead, saying to
run `panopticon login --server <that server>`.

If neither source has a value, the bridge still starts. Each `report_progress` call then fails with an error
naming the missing variable and saying to run `panopticon login`. If the saved file can't be read, the bridge
treats it as missing and says so once on stderr, without its content.

## Claude Code

Log the machine in first. Then add the bridge to the repo's `.mcp.json`. It needs no settings, because it
reads the saved login. Claude Code starts stdio servers in the project directory, which is how the bridge finds
the repo.

```json
{
  "mcpServers": {
    "panopticon": {
      "type": "stdio",
      "command": "panopticon",
      "args": ["mcp"]
    }
  }
}
```

Or add the bridge for just yourself, without a file:

```sh
claude mcp add panopticon --scope local -- panopticon mcp
```

With `panopticon login` you don't need an `env` block at all. To point one repo somewhere else, set the
variables in its `env` block instead, expanded from your shell so the token never lands in the file. Give each
one an empty default, for example `"PANOPTICON_TOKEN": "${PANOPTICON_TOKEN:-}"`. Claude Code passes a plain
`${PANOPTICON_TOKEN}` through literally when the variable is unset, and that text then overrides the saved
login and is rejected. A blank value counts as unset, so the bridge falls back to the saved login.

**Local development.** A server running in Development also accepts its bootstrap token, `dev-bootstrap-token`.
Reports made with it have no member. It's a shortcut for working on Panopticon itself:

```sh
PANOPTICON_SERVER_URL=http://localhost:7480 PANOPTICON_TOKEN=dev-bootstrap-token claude
```

Then ask the agent to call `report_progress`. It should pass `change` (the folder under `openspec/changes/`)
and, optionally, `section` (the `## N.` it is working on). The reply states overall and current-section
progress, e.g. `Overall 7/20 · section 2 Core 2/5`.

## Other agents

Any MCP client that can launch a stdio server works the same way. Its command is `panopticon` with the
arguments `["mcp"]`, and it starts in the repo (or any directory inside it). It uses the saved login, or the two
variables above.

## Troubleshooting

The bridge logs to stderr. Stdout carries nothing but the MCP protocol. Tool errors say what went wrong:

- **"No git repository was found"**: the agent started the bridge outside the repo.
- **"no 'origin' remote"**: add a remote. The repo identity comes from it.
- **"No change '…' was found: looked for the folder '…'"**: the change name doesn't match a folder under
  `openspec/changes/`.
- **"Couldn't reach the Panopticon server at …"**: check `PANOPTICON_SERVER_URL`, and that the server is up.
- **"isn't configured: … not set"**: run `panopticon login`, or set the variables it names.
- **"saved login is for …, not PANOPTICON_SERVER_URL"**: the environment points at another server. Log in to
  that server with `panopticon login --server <url>`, or remove `PANOPTICON_SERVER_URL`.
- **"rejected the saved login's token"**: the token was revoked, or its member was removed. Run
  `panopticon login` for a new one.
- **"rejected the token from PANOPTICON_TOKEN"**: the variable is wrong, and it takes precedence over the saved
  login. Correct it, or remove it from the agent's MCP server environment. Check for an unexpanded
  `${PANOPTICON_TOKEN}` in `.mcp.json` (see above).
