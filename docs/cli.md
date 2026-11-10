# The `panopticon` CLI

`panopticon login` gives this machine an API token, approved in the dashboard. `panopticon mcp` is a local
MCP server that agents talk to over stdio. It offers three tools, `report_progress`, `list_projects` and
`tag_change`, and forwards each call to the Panopticon server's online MCP endpoint (`/mcp`). `panopticon tags`
keeps the personal tags this machine's reports carry.

The agent only names the change. The bridge supplies the rest from the local working copy:

- **The repo identity**, from the `origin` remote. Any host works (GitHub, GitLab subgroups, Azure DevOps over
  HTTPS or SSH). Credentials in the URL are dropped and never sent or logged.
- **The change's documents**: every Markdown and YAML file in `openspec/changes/<change>/` under the repo
  root, which is the git top-level of the bridge's working directory. See [What the bridge sends](#what-the-bridge-sends).
- **The agent**: the MCP client name the agent gave when it connected (for example `claude-code`), plus this
  machine's name.
- **The Project**, from `.panopticon.json` at the repo root, if there is one. See [Linking a repo to a
  Project](#linking-a-repo-to-a-project).
- **Your personal tags**, from the config file. See [Personal tags](#personal-tags).

## Linking a repo to a Project

A repo's work belongs to a Project when the repo root holds a `.panopticon.json` naming it:

```json
{ "project": "6f1c2a4e-…" }
```

- **What it holds:** `project` is the Project's id, not its code. Codes and names can't be confused with ids, and
  a renamed Project keeps its id. Other fields are ignored, so later versions can add to the file.
- **Commit it.** Everyone who works in the repo then reports into the same Project, and their agents' cards appear on
  the dashboard of every member of that Project.
- **Without it,** reports are your own personal work: only you see them.
- **When it's read:** on every report, with no caching, so adding or editing the file takes effect on the next one.
  Work you reported before linking moves into the Project with its history. If a teammate linked the repo first,
  your personal copy of the change leaves your dashboard and stays in your history as "Linked to <code>".
- **Finding the id:** ask the agent to link the repo. It calls `list_projects`, which returns the Projects you can
  report into, each with its id, name, code and your role, and writes the file. Or copy the file from the Project's
  page in the dashboard.

You can report into any Project you're a member of, in any role. A report naming a Project you aren't in, or one
that doesn't exist, is refused, and nothing is recorded.

## Personal tags

Personal tags are your own labels for your work, such as `dev`, `urgent` or `language: csharp`. Each machine keeps
its own list, and every report from it carries them. Only you see them, on your cards, change pages and history,
and in the Tags filter.

```sh
panopticon tags                              # one per line, or "No personal tags."
panopticon tags add dev "language: csharp"   # prints the list afterwards
panopticon tags remove DEV                   # tags compare without regard to case
```

- **What a tag is:** a key, or a key and a value after the first `:`. Spaces around either are trimmed. A key is
  ASCII letters, digits, `-`, `_` and `.`. A value can hold anything but a line break. Each is at most 40
  characters.
- **All or nothing:** a malformed tag, one that's already saved, a 21st tag (20 at most), or removing one that
  isn't saved makes the command exit 1, saying why. Nothing in the file changes.
- **Where they're kept:** under `"tags"` in the config file, beside the login (see [Configuration](#configuration)).
  `panopticon tags` keeps the login, and `panopticon login` keeps the tags.
- **When they're sent:** the bridge reads them on every report, so an edit counts from the next report, with no
  restart. With none saved, a report sends an empty list, which clears this machine's tags on that change.
  - **One change, several machines:** your tags on it are the ones all your machines sent.
  - **Archived changes:** a change keeps the tags it had when it was archived.
- **The agent never sees them.** It isn't asked for them, and they aren't in any tool's reply.
- **A hand-edited file:** if the file holds a tag that isn't valid, reports fail with an error naming the file and
  the tag, and nothing is sent, rather than clearing your tags. Fix it, or run `panopticon tags remove` with the
  bad entry's exact text.
- **A file that can't be read** (not valid JSON, or no permission): reports still go, without your tags, so the
  server keeps the ones it has. The bridge logs a warning naming the file on stderr. `panopticon tags` refuses to
  edit such a file, so nothing in it is lost.

Project tags are different: a Project's owners define them, and they're shared by everyone in the Project. To put
them on changes, use the dashboard, or ask the agent: it calls `tag_change`.

## Tagging a change

`tag_change` puts a Project's existing tags on one of the repo's changes, or takes them off.

- **What the agent gives:** `change`, plus `add` and `remove` as lists of tag texts, as `list_projects` shows them
  (`"bug"`, `"area: onboarding"`).
- **What the bridge fills in:** the Project from `.panopticon.json`, and the repo from the `origin` remote. The agent
  can pass `project` and `repo` to override them.
- **What it can't do:** an agent can only use tags the Project already has. It can't create one.
- **Who can use it:** the token must belong to one of the Project's owners or editors.
- **The reply:** the change's project tags afterwards, for example `walking-skeleton now has PAN/area: cli, PAN/bug.`
- **Which change:** the active one with that name, or, if there's none, the latest archived one.

## What the bridge sends

On every report, the bridge reads the change's folder as it is at that moment and sends all of it, so the
dashboard's change page always shows the latest proposal, design, tasks, spec deltas and devlog notes.

- **Which files:** every file ending `.md`, `.yaml` or `.yml` (in any case), in the folder and its subfolders:
  `proposal.md`, `design.md`, `tasks.md`, `specs/**`, `devlog/*.md`, `.openspec.yaml`, and any others. Each is
  sent under its path relative to the folder, with `/` separators. Other files, such as images, are skipped, and the
  report still succeeds. `tasks.md` is required.
- **Links:** symbolic links and other reparse points, to files or folders, are skipped, so nothing outside the
  change's folder is read.
- **Text:** files are read as UTF-8. A byte order mark is dropped. A file that isn't valid UTF-8 fails the report,
  naming the file.
- **Paths:** the server's path rules are checked first: at most 512 characters, and no control characters. A path
  that breaks them fails the report, naming the file.
- **Limits:** at most 256 KB (262,144 bytes) for any file except `tasks.md`, 1 MiB for `tasks.md`, and 4 MB
  (4,194,304 bytes) in total. A report over a limit fails with an error naming the file and its size, or the
  total, and **nothing is sent**. The server checks the same limits again.

## Archives

When `/opsx:archive` moves a change to `openspec/changes/archive/<YYYY-MM-DD>-<change>/`, Panopticon moves it from
the live dashboard to the history. The bridge notices an archive in two ways, and both send the same archived
report: the archive folder's documents, marked with the folder's name.

- **Reporting after an archive.** When a report names a change with no folder under `openspec/changes/`, the
  bridge looks in `archive/` for `<YYYY-MM-DD>-<change>`, matching the name exactly, and uses the latest date if
  there are several. If there's none, the error names both places it looked.
- **The watcher.** While `panopticon mcp` runs, it watches `openspec/changes/` for the changes **it has reported on
  in this session**. When one of their folders moves to `archive/`, the bridge sends the archived report itself
  about a second later, as the agent that last reported that change. The agent doesn't need to do anything.
  - Changes this bridge never reported on are left alone. So are archives made while no bridge was running, until
    something reports that change name again (the first way).
  - A failure, such as an unreachable server, is logged to stderr, and the bridge keeps serving. It retries the
    archived report every 30 seconds until it gets through. The agent's next `report_progress` for that change
    reports it archived too.
  - The bridge sends one report at a time for each change, so an archived report never overtakes one the agent
    started before the folder moved.

Reporting an archived change again changes nothing: the server keeps it as it was when it was archived. A new
change later created with the same name starts a fresh card.

## Install

The CLI is one native executable with no .NET runtime needed. Until releases are published, build it from
this repo with `make install`. That builds it (`make aot`, which writes `artifacts/cli/panopticon`) and copies
it to `~/.local/bin/panopticon`, or to another directory with `make install INSTALL_DIR=…`. It says if that
directory isn't on your `PATH`. Run it again after pulling to update, then restart your agent so its
`panopticon mcp` bridge runs the new build. You can also use `artifacts/cli/panopticon`'s full path as the
`command` below.

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

The login is saved as `{"serverUrl": …, "token": …}`, with any [personal tags](#personal-tags) under `"tags"`,
in:

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
progress, e.g. `Overall 7/20 · section 2 Core 2/5`. For an archived change it ends with the archive folder, e.g.
`Overall 20/20 · section 4 Docs 3/3 · archived as 2026-11-08-team-auth`.

## Other agents

Any MCP client that can launch a stdio server works the same way. Its command is `panopticon` with the
arguments `["mcp"]`, and it starts in the repo (or any directory inside it). It uses the saved login, or the two
variables above. `list_projects` takes no arguments and uses the same server and token as reports, and so does
`tag_change`.

## Troubleshooting

The bridge logs to stderr. Stdout carries nothing but the MCP protocol. Tool errors say what went wrong:

- **"No git repository was found"**: the agent started the bridge outside the repo.
- **"no 'origin' remote"**: add a remote. The repo identity comes from it.
- **"No change '…' was found: looked for the folder '…', and for an archived '…'"**: the change name doesn't match
  a folder under `openspec/changes/`, or an archived one under `openspec/changes/archive/`.
- **"'…' is … bytes; the limit is …"** or **"The change's documents total …"**: a document, or the whole folder, is
  over the size limits above. Nothing was sent. Move large files out of the change's folder.
- **"'…' isn't valid UTF-8"** or **"'…' can't be sent: its path …"**: rename the file, or re-save it as UTF-8.
- **".panopticon.json isn't valid JSON"**, **"has no \"project\""** or **"\"project\" must be a Project id from
  list_projects"**: fix the link file (see [Linking a repo to a Project](#linking-a-repo-to-a-project)). Nothing
  was sent. The bridge never falls back to a personal report, so a typo can't hide the work.
- **"Project … wasn't found, or you aren't a member of it"**: the link file names a Project you can't report into.
  Ask an owner to invite you, or call `list_projects` for the ones you can.
- **"The personal tags in … aren't valid"**: fix the config file, or run `panopticon tags remove` with the bad
  entry's exact text. Nothing was sent.
- **"The config at … can't be read"** (from `panopticon tags`, or as a warning in the bridge's log): fix the file,
  or run `panopticon login` to write a fresh one, then add your tags again.
- **"This repo isn't linked to a Project"** (from `tag_change`): only a linked repo's changes have project tags. Link
  it first.
- **"The Panopticon server refused the tag change: …"**: the server's reason follows. For an unknown tag it lists the
  Project's tags, and for a viewer's token it says only owners and editors can tag.
- **"Couldn't reach the Panopticon server at …"**: check `PANOPTICON_SERVER_URL`, and that the server is up.
- **"isn't configured: … not set"**: run `panopticon login`, or set the variables it names.
- **"saved login is for …, not PANOPTICON_SERVER_URL"**: the environment points at another server. Log in to
  that server with `panopticon login --server <url>`, or remove `PANOPTICON_SERVER_URL`.
- **"rejected the saved login's token"**: the token was revoked, or its member was removed. Run
  `panopticon login` for a new one.
- **"rejected the token from PANOPTICON_TOKEN"**: the variable is wrong, and it takes precedence over the saved
  login. Correct it, or remove it from the agent's MCP server environment. Check for an unexpanded
  `${PANOPTICON_TOKEN}` in `.mcp.json` (see above).
