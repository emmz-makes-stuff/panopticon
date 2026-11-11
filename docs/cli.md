# The `panopticon` CLI

`panopticon login` gives this machine an API token, approved in the dashboard. `panopticon mcp` is a local
MCP server that agents talk to over stdio. It offers three tools, `report_progress`, `list_projects` and
`tag_change`, and forwards each call to the Panopticon server's online MCP endpoint (`/mcp`). `panopticon tags`
keeps the personal tags this machine's reports carry. `panopticon hook` sends Claude Code sessions' activity and
prompts, and `panopticon away` turns away mode on or off (see [Session activity and away
mode](#session-activity-and-away-mode)). `panopticon update` installs the build your server serves (see
[Install](#install) and [Update](#update)).

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

The CLI is one native executable with no .NET runtime needed. Your Panopticon server hands out the build of its
own release. The **Set up a machine** guide on the Tokens page shows the command for your OS, with a Copy button, and
then the log-in and connect steps below.

On Linux or macOS:

```sh
curl -fsSL https://panopticon.example.com/install.sh | sh
```

On Windows, in PowerShell:

```powershell
irm https://panopticon.example.com/install.ps1 | iex
```

The script picks your platform's build, checks its SHA-256 and checks that it runs before putting it in place. Then
it prints the `panopticon login --server …` command to run next. Every download comes from the server you fetched
the script from.

- **Where it goes:** on Linux and macOS, `~/.local/bin/panopticon`, or `$PANOPTICON_INSTALL_DIR/panopticon` when
  that's set. If the directory isn't on your `PATH`, the script says so and shows the line to add. On Windows,
  `%LOCALAPPDATA%\Programs\panopticon\panopticon.exe`, and that directory is added to your user `PATH`, so open a
  new terminal afterwards. Running the script again replaces the CLI that's there.
- **Platforms:** Linux x64 and arm64 with glibc 2.35 or later (Ubuntu 22.04+, Debian 12+), macOS on Apple Silicon,
  and Windows x64. musl Linux (Alpine), Intel Macs and Windows on Arm aren't supported. The script says so and
  installs nothing.
- **A check fails** (a bad checksum, or a build that won't run here, such as on an older glibc): nothing is installed,
  and an existing CLI is left as it was.
- **A server built without CLI builds** (a local `docker build`, say) answers `/install.sh` with a note saying so.
  Build the CLI from source instead: `make install` in this repo builds it (`make aot`, which writes
  `artifacts/cli/panopticon`) and copies it to `~/.local/bin`, or to `make install INSTALL_DIR=…`. You can also use
  `artifacts/cli/panopticon`'s full path as the `command` below.

`panopticon --version` prints the version. A build from source is `0.0.0-dev`.

## Update

```sh
panopticon update
```

This installs the build your server serves, so the CLI matches the server it reports to, whether that's newer or
older. It does nothing if the versions already match. It uses the same server as `panopticon login`: `--server
<url>`, then `PANOPTICON_SERVER_URL`, then the server you last logged in to. It needs no token.

- It checks the download's size and SHA-256 before replacing anything. If anything fails, the old CLI keeps working,
  and it says what went wrong.
- It only updates over https, except from this machine (`localhost`, `127.0.0.1`, `::1`). Over plain http, anyone on
  the way could swap the build together with its checksum.
- Afterwards, restart your agent, so its `panopticon mcp` bridge runs the new version.
- On Windows, the running `panopticon.exe` is renamed to `panopticon.exe.old`, and the new one takes its place. The
  next `panopticon` command deletes the old one.
- A CLI built from source updates the same way. To stay on your own builds, run `make install` again after pulling.

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

Two more files sit beside `config.json`, with the same permissions:

- **`worktrees.json`**: each working tree's root and the change the bridge last reported from it, so
  [`panopticon hook`](#session-activity-and-away-mode) knows a session's change. The bridge writes it after every
  report the server accepts, and drops a working tree's entry when that change is reported archived. It's a cache:
  delete it and the next report rebuilds it. If it can't be written, the report still goes, and the bridge logs a
  warning.
- **`hook.log`**: where `panopticon hook` writes its failures, one line each, never the token. At 1 MB it's moved
  aside to `hook.log.1`.

## Claude Code

Log the machine in first. Then the simplest route is the Panopticon plugin, from the public
[`emmz-makes-stuff/panopticon-skills`](https://github.com/emmz-makes-stuff/panopticon-skills) marketplace. Run these in
Claude Code:

```text
/plugin marketplace add emmz-makes-stuff/panopticon-skills
/plugin install panopticon@panopticon-skills
/panopticon:setup
```

The plugin does three things:

- it registers the `panopticon mcp` bridge;
- it nudges the agent to call `report_progress` whenever a change's `tasks.md` changes;
- it adds `/panopticon:setup`, which checks the CLI, the login and the repo, and says what's missing.

It uses the machine's saved login, so the commands are the same for every server. Don't also add a `panopticon`
server to the repo's `.mcp.json`: two copies of the bridge would both report.

**Without the plugin**, add the bridge to the repo's `.mcp.json`. It needs no settings, because it reads the saved
login. Claude Code starts stdio servers in the project directory, which is how the bridge finds the repo.

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

## Session activity and away mode

With the plugin's hooks installed, each Claude Code session shows on its change's card: what it's doing, or what
it's waiting on. When you're away from the keyboard, its questions and permission prompts wait for you to answer
them on the dashboard.

### `panopticon hook`

The plugin runs `panopticon hook` from Claude Code's hooks, with the event on stdin. You don't run it yourself.

- **What it sends:** the session's state (working, waiting on a question, a permission prompt or you, or ended),
  and a one-line summary of the tool in use, of at most 120 characters. The summary is the Bash tool's own
  description, or the tool and a file name, for example `Editing tasks.md` or `Running agent: Review the diff`. A
  command, a file's content and any other tool input never leave the machine.
- **Which change:** the one the bridge last reported from the session's working tree (see `worktrees.json` under
  [Configuration](#configuration)), or else `<name>` from a `change/<name>` branch. With neither, it sends nothing.
  The repo identity and Project link come from the same places as reports.
- **It never gets in the way:** apart from a prompt that's waiting for an answer, it finishes within 5 seconds and
  exits 0 with nothing on stdout, whatever happens: no login, the server down, a refusal, or input it can't read.
  Failures go to `hook.log` (see [Configuration](#configuration)).

### Prompts

A question (`AskUserQuestion`) or a permission prompt is posted to the server, which decides whether to hold it:

- **You're not away:** the terminal asks as usual, and the card shows the session waiting with "answer in the
  terminal". A permission prompt sends only the tool's name, never its input.
- **You're away:** the hook holds the prompt and waits, and the terminal shows nothing. A permission prompt's input
  goes up only now, as a preview cut to 2 KB. Once you, or an owner of the change's Project, answer it on the
  dashboard, the session carries on with that answer. Allowing a tool runs it once and never adds a permission rule;
  denying it gives the session your message.
- **The terminal takes over** when you turn away mode off, after 8 hours, or when the server can't be reached for a
  minute.
- **Too big for the dashboard:** a question with more than four questions or four options, an option preview over
  8 KB, or a prompt over 64 KB in all, goes to the terminal. The card shows the session waiting on a question it
  can't show.

### `panopticon away`

```sh
panopticon away        # prints whether you're away, and since when
panopticon away on     # prompts from your sessions now wait for the dashboard
panopticon away off    # held prompts go back to their terminals
```

Away mode is yours, not the machine's: it covers all your sessions on every machine, and the dashboard's header has
the same switch. It uses the same server and token as reports. Without a login it exits 1 and says to run
`panopticon login`.

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
- **"panopticon update needs an https server"**: the server URL is plain http and isn't this machine. Use the
  server's https address, or reinstall with its install script.
- **"has no CLI builds"** (from `panopticon update`, the install scripts or the Tokens page): this server was built
  without them. Build from source with `make install`.
- **"Checksum mismatch"** (from `panopticon update` or `install.sh`): the download doesn't match the server's
  manifest. Nothing was changed. Try again, and if it persists, check for a proxy rewriting downloads.
- **"the downloaded CLI doesn't run on this machine"** (from `install.sh`): usually a glibc older than 2.35. Nothing
  was installed.
- **"isn't configured: … not set"**: run `panopticon login`, or set the variables it names.
- **"saved login is for …, not PANOPTICON_SERVER_URL"**: the environment points at another server. Log in to
  that server with `panopticon login --server <url>`, or remove `PANOPTICON_SERVER_URL`.
- **"rejected the saved login's token"**: the token was revoked, or its member was removed. Run
  `panopticon login` for a new one.
- **"rejected the token from PANOPTICON_TOKEN"**: the variable is wrong, and it takes precedence over the saved
  login. Correct it, or remove it from the agent's MCP server environment. Check for an unexpanded
  `${PANOPTICON_TOKEN}` in `.mcp.json` (see above).
- **A session doesn't show on its card**: look in `hook.log` (see [Configuration](#configuration)). No line at all
  usually means the hook found no change: report the change once with the bridge, or work on a `change/<name>`
  branch. A card shows sessions only for changes it already has, so the change must have been reported first.
