# The `panopticon` CLI

`panopticon mcp` is a local MCP server that agents talk to over stdio. It offers one tool,
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

## Configuration

| Variable | Meaning |
| --- | --- |
| `PANOPTICON_SERVER_URL` | The server's base URL, e.g. `https://panopticon.example.com`. A URL already ending in `/mcp` is used as-is. |
| `PANOPTICON_TOKEN` | The bearer token for the server. For now this is the server's `Panopticon:BootstrapToken`. |

If either one is missing, the bridge still starts. Each `report_progress` call then fails with an error naming
the missing variable.

## Claude Code

Add the bridge to the repo's `.mcp.json`. Claude Code starts stdio servers in the project directory, which is
how the bridge finds the repo.

```json
{
  "mcpServers": {
    "panopticon": {
      "type": "stdio",
      "command": "panopticon",
      "args": ["mcp"],
      "env": {
        "PANOPTICON_SERVER_URL": "${PANOPTICON_SERVER_URL:-http://localhost:7480}",
        "PANOPTICON_TOKEN": "${PANOPTICON_TOKEN}"
      }
    }
  }
}
```

The token is expanded from your shell environment, so it never lands in the file. Export it before starting
`claude`:

```sh
export PANOPTICON_TOKEN=dev-bootstrap-token   # the local development server's token
```

Or add the bridge for just yourself, without a file:

```sh
claude mcp add panopticon --scope local \
  --env PANOPTICON_SERVER_URL=http://localhost:7480 \
  --env PANOPTICON_TOKEN="$PANOPTICON_TOKEN" \
  -- panopticon mcp
```

Then ask the agent to call `report_progress`. It should pass `change` (the folder under `openspec/changes/`)
and, optionally, `section` (the `## N.` it is working on). The reply states overall and current-section
progress, e.g. `Overall 7/20 · section 2 Core 2/5`.

## Other agents

Any MCP client that can launch a stdio server works the same way. Its command is `panopticon` with the
arguments `["mcp"]`, it sets the two variables above, and it starts in the repo (or any directory inside it).

## Troubleshooting

The bridge logs to stderr. Stdout carries nothing but the MCP protocol. Tool errors say what went wrong:

- **"No git repository was found"**: the agent started the bridge outside the repo.
- **"no 'origin' remote"**: add a remote. The repo identity comes from it.
- **"No change '…' was found: looked for the folder '…'"**: the change name doesn't match a folder under
  `openspec/changes/`.
- **"Couldn't reach the Panopticon server at …"**: check `PANOPTICON_SERVER_URL`, and that the server is up.
- **"rejected the token"**: check `PANOPTICON_TOKEN`.
