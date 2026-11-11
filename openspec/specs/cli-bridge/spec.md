# cli-bridge Specification
## Purpose

The CLI's local stdio MCP server. Any MCP-capable agent can report progress through it, while the bridge
itself supplies the repo identity and the change's documents from the local working copy.
## Requirements

### Requirement: Stdio MCP server
`panopticon mcp` SHALL run an MCP server over stdin/stdout that offers a `report_progress` tool taking
the change name and, optionally, the current section number, and a `list_projects` tool taking no arguments. It
SHALL write nothing but MCP protocol messages to stdout, and its logs SHALL go to stderr.

#### Scenario: Agent lists tools
- **WHEN** an MCP client starts `panopticon mcp` and lists its tools
- **THEN** the list includes `report_progress` with `change` (required) and `section` (optional), and
  `list_projects`

### Requirement: Configuration
The bridge SHALL take the server URL from `PANOPTICON_SERVER_URL` and the token from `PANOPTICON_TOKEN`. When
a variable is unset, it SHALL use the value saved by `panopticon login`. A saved token SHALL be used only for
the server it was saved with, so it is never sent to another server. If either is still missing,
`report_progress` SHALL return an error naming it and saying to run `panopticon login`, and the bridge SHALL
keep running.

#### Scenario: Missing token
- **WHEN** `PANOPTICON_TOKEN` is unset, no login is saved, and the agent calls `report_progress`
- **THEN** the tool returns an error naming `PANOPTICON_TOKEN` and `panopticon login`, and the bridge keeps
  serving

#### Scenario: Saved login
- **WHEN** neither variable is set and `panopticon login` has saved a server and token
- **THEN** reports go to the saved server with the saved token

#### Scenario: Environment overrides the saved login
- **WHEN** a login is saved and `PANOPTICON_TOKEN` is also set
- **THEN** reports use the token from `PANOPTICON_TOKEN`

#### Scenario: Environment server with a login saved for another server
- **WHEN** a login is saved for `https://a.example.com`, `PANOPTICON_SERVER_URL` is `https://b.example.com`, and
  `PANOPTICON_TOKEN` is unset
- **THEN** no request carries the saved token, and the tool returns an error naming both servers and saying to
  run `panopticon login --server https://b.example.com`

### Requirement: Repo root
The bridge SHALL treat the working tree's git top-level directory, found from its working directory, as
the repo root.

#### Scenario: Started in a subdirectory
- **WHEN** the bridge's working directory is `<repo>/src/app`
- **THEN** it reads `openspec/` from `<repo>`

#### Scenario: Not a git repo
- **WHEN** the working directory isn't inside a git working tree
- **THEN** `report_progress` returns an error saying no git repository was found

### Requirement: Repo identity from git remote
The bridge SHALL get the repo identity from the `origin` remote URL, accepting SSH, scp-style and HTTPS
forms from any host. Owner SHALL be every path segment but the last, and repo SHALL be the last segment
without `.git`. Azure DevOps markers (`v3/`, `_git/`) SHALL be dropped.

#### Scenario: GitHub SSH
- **WHEN** origin is `git@github.com:emmz/panopticon.git`
- **THEN** the identity is host `github.com`, owner `emmz`, repo `panopticon`

#### Scenario: GitHub HTTPS
- **WHEN** origin is `https://github.com/emmz/panopticon`
- **THEN** the identity is host `github.com`, owner `emmz`, repo `panopticon`

#### Scenario: GitLab subgroup
- **WHEN** origin is `https://gitlab.com/acme/platform/api.git`
- **THEN** the identity is host `gitlab.com`, owner `acme/platform`, repo `api`

#### Scenario: Azure DevOps HTTPS
- **WHEN** origin is `https://dev.azure.com/acme/Nova/_git/nova-api`
- **THEN** the identity is host `dev.azure.com`, owner `acme/Nova`, repo `nova-api`

#### Scenario: Azure DevOps SSH
- **WHEN** origin is `git@ssh.dev.azure.com:v3/acme/Nova/nova-api`
- **THEN** the identity is host `dev.azure.com`, owner `acme/Nova`, repo `nova-api`

#### Scenario: Credentials in URL
- **WHEN** origin is `https://user:secret@github.com/emmz/panopticon.git`
- **THEN** the identity is host `github.com`, owner `emmz`, repo `panopticon`, and the credentials are
  never sent to the server or logged

#### Scenario: No origin remote
- **WHEN** the repo has no `origin` remote
- **THEN** `report_progress` returns an error saying a remote is required

### Requirement: Change documents read locally
The bridge SHALL read the change's folder `openspec/changes/<change>/` from the repo root, and forward every
Markdown and YAML file in it (including `specs/**` and `devlog/`) by its path relative to that folder, with
`tasks.md` required. It SHALL refuse change names that could leave `openspec/changes/`, and SHALL NOT follow links
out of the change's folder.

#### Scenario: Change exists
- **WHEN** the agent reports change `walking-skeleton` and its `tasks.md` exists
- **THEN** the bridge forwards that file's current content

#### Scenario: Whole folder forwarded
- **WHEN** the change's folder holds `proposal.md`, `design.md`, `tasks.md`, `specs/cli-bridge/spec.md`,
  `devlog/20261108-1515-section-1-done.md` and `.openspec.yaml`
- **THEN** the report carries all six, each under its relative path

#### Scenario: Other files skipped
- **WHEN** the change's folder also holds `diagram.png`
- **THEN** the report doesn't carry it, and the report still succeeds

#### Scenario: Change missing
- **WHEN** the agent reports a change with no folder under `openspec/changes/` and none archived under that name
- **THEN** `report_progress` returns an error naming the path it looked for

#### Scenario: Path traversal
- **WHEN** the agent reports change `../../etc`
- **THEN** `report_progress` returns an invalid-name error and reads nothing

#### Scenario: Link out of the folder
- **WHEN** the change's folder holds `notes.md`, which is a symbolic link to a file outside it
- **THEN** the report doesn't carry `notes.md`

### Requirement: Agent identity
The bridge SHALL identify the reporting agent by the MCP client name the agent gave at initialisation,
plus the machine's host name.

#### Scenario: Claude Code on a VM
- **WHEN** Claude Code (client name `claude-code`) runs the bridge on host `vm-1`
- **THEN** reports identify the agent as `claude-code` on `vm-1`

### Requirement: Forwarding to the server
The bridge SHALL forward each report to the server's online MCP endpoint with the configured bearer
token, and SHALL return the server's computed progress to the agent. Server errors SHALL come back to the
agent as tool errors, and the bridge SHALL keep running.

#### Scenario: Successful report
- **WHEN** the server accepts a report
- **THEN** the agent receives a result stating overall and current-section progress

#### Scenario: Server unreachable
- **WHEN** the server can't be reached
- **THEN** the tool returns an error naming the server URL, and the bridge keeps serving later calls

#### Scenario: Token rejected
- **WHEN** the server responds 401
- **THEN** the tool returns an error saying the token was rejected and where it came from, without echoing the
  token: for a token from `PANOPTICON_TOKEN`, to correct or remove that variable; for the saved login's token,
  to run `panopticon login`

### Requirement: Single self-contained binary
The CLI SHALL be published as one self-contained native executable per platform that runs without a .NET
runtime installed.

#### Scenario: Runs on a clean VM
- **WHEN** the published Linux binary is copied to a machine without .NET and runs `panopticon mcp`
- **THEN** the bridge starts and answers an MCP tool listing

### Requirement: Document size limits
The bridge SHALL refuse to send a report whose documents include a file other than `tasks.md` over 256 KB, a
`tasks.md` over 1 MiB (as `task-progress` already limits it), a total over 4 MB, or more than 500 documents. The error SHALL name the offending file and its size, the total, or the count, and nothing SHALL be sent.

#### Scenario: One file too large
- **WHEN** `devlog/huge.md` is 300 KB
- **THEN** `report_progress` returns an error naming `devlog/huge.md` and the 256 KB limit, and sends nothing

#### Scenario: Folder too large
- **WHEN** the change's documents total 5 MB, each under 256 KB
- **THEN** `report_progress` returns an error naming the 4 MB total limit, and sends nothing

#### Scenario: Too many files
- **WHEN** the change's folder holds 501 Markdown files
- **THEN** `report_progress` returns an error naming the 500-document limit, and sends nothing

### Requirement: Archived change reported
When the reported change has no folder under `openspec/changes/` but has one under `openspec/changes/archive/`
named `<YYYY-MM-DD>-<change>`, the bridge SHALL report that folder's documents, marked archived with the folder's
name. When there are several, it SHALL use the latest date. The tool's reply SHALL say that the change was
archived, and under which folder.

#### Scenario: Report after archive
- **WHEN** `team-auth` was moved to `openspec/changes/archive/2026-11-08-team-auth/` and the agent reports
  `team-auth`
- **THEN** the bridge reports that folder's documents, marked archived as `2026-11-08-team-auth`, and returns the
  final progress, saying it was archived as `2026-11-08-team-auth`

#### Scenario: Archived twice under one name
- **WHEN** both `archive/2026-03-01-cleanup/` and `archive/2026-11-08-cleanup/` exist and the agent reports
  `cleanup`
- **THEN** the bridge reports `archive/2026-11-08-cleanup/`

### Requirement: Watching for archives
While `panopticon mcp` runs, it SHALL watch `openspec/changes/` for the changes it has reported in this session.
When one of those folders moves to `openspec/changes/archive/`, the bridge SHALL report it archived by itself, as
the last agent that reported it, within a few seconds. A watcher failure SHALL be logged to stderr and SHALL NOT
stop the bridge. When the archived report fails, the bridge SHALL retry it every 30 seconds while it runs, until it
succeeds.

#### Scenario: Archive while the bridge runs
- **WHEN** Claude Code reported `team-auth` through the bridge, then `/opsx:archive` moves its folder to `archive/`
- **THEN** the bridge reports `team-auth` archived, as `claude-code` on this machine, without the agent calling a
  tool

#### Scenario: Unreported change archived
- **WHEN** a change this bridge never reported on is moved to `archive/`
- **THEN** the bridge reports nothing about it

#### Scenario: Server unreachable at archive time
- **WHEN** `team-auth` is archived while the server can't be reached, and the server comes back a minute later
- **THEN** the bridge's retry reports `team-auth` archived, without the agent calling a tool

### Requirement: One report at a time per change
The bridge SHALL send one report at a time for each change, whether the agent or the archive watcher started it, so
an archived report never overtakes a report already read from the active folder.

#### Scenario: Report in flight at archive time
- **WHEN** the agent's report of `team-auth` has been read from its active folder and is still being sent when
  `/opsx:archive` moves the folder
- **THEN** the archived report is sent after it, and the dashboard ends with no `team-auth` card

#### Scenario: Server down at archive time
- **WHEN** the watcher's archived report fails because the server can't be reached
- **THEN** the failure is logged to stderr, the bridge keeps serving, and the next `report_progress` for that
  change reports it archived

### Requirement: Project link file
The bridge SHALL read `.panopticon.json` at the repo root on each report. The file is a JSON object whose `project`
field is a Project id, e.g. `{ "project": "6f1c…" }`. When it's there, the report SHALL carry that id. With no file,
the report SHALL carry no Project. Other fields SHALL be ignored, so later changes can add to the file.

#### Scenario: Linked repo
- **WHEN** the repo root holds `.panopticon.json` naming `PAN`'s id, and the agent calls `report_progress`
- **THEN** the report carries `PAN`'s id

#### Scenario: No link file
- **WHEN** the repo root has no `.panopticon.json`
- **THEN** the report carries no Project, and it's accepted into the reporter's personal scope

### Requirement: Malformed link file
When `.panopticon.json` isn't valid JSON, or its `project` is missing or not a UUID, `report_progress` SHALL return
an error naming the file and what's wrong with it, SHALL send nothing, and the bridge SHALL keep running. It SHALL
NOT fall back to an unlinked report, so a typo can't hide the work in a personal scope.

#### Scenario: Not a UUID
- **WHEN** `.panopticon.json` is `{ "project": "PAN" }`
- **THEN** the tool returns an error naming `.panopticon.json` and saying `project` must be a Project id from
  `list_projects`, and nothing is sent

### Requirement: Listing Projects through the bridge
The bridge's `list_projects` SHALL forward to the server's `list_projects` with the same server and token as reports
(see Configuration), and return its result. A missing token, an unreachable server or a refusal SHALL be a tool error
saying what went wrong, as for `report_progress`, and the bridge SHALL keep running.

#### Scenario: Agent links a repo
- **WHEN** an agent calls the bridge's `list_projects`
- **THEN** it gets the reporter's Projects with their ids, names, codes and roles, from which it can write
  `.panopticon.json`

#### Scenario: No login
- **WHEN** no token is configured and the agent calls `list_projects`
- **THEN** the tool returns an error naming `PANOPTICON_TOKEN` and `panopticon login`

### Requirement: Personal tags in the config
The CLI config file SHALL hold an optional list of personal tags beside the saved login. `panopticon tags` SHALL list
them; `panopticon tags add <tag>…` and `panopticon tags remove <tag>…` SHALL edit them, keeping the saved login and
the file's permissions. A malformed tag, a duplicate or a 21st tag SHALL be refused, saying why, and change nothing.

#### Scenario: Adding a tag
- **WHEN** a member runs `panopticon tags add "language: csharp"` with `dev` already saved
- **THEN** `panopticon tags` lists `dev` and `language: csharp`, and the saved login is unchanged

#### Scenario: Removing a tag
- **WHEN** a member runs `panopticon tags remove DEV` with `dev` saved
- **THEN** `dev` is removed, since tags compare without regard to case

#### Scenario: A malformed tag
- **WHEN** a member runs `panopticon tags add "has space"`
- **THEN** it exits non-zero with an error naming the tag, and the file is unchanged

#### Scenario: A malformed file
- **WHEN** the config file's tags list holds a malformed tag, and the agent calls `report_progress`
- **THEN** the tool returns an error naming the config file and the tag, without the file's token, and the bridge
  keeps serving

### Requirement: Personal tags sent with reports
The bridge SHALL send the config's personal tags with every `report_progress`, read when it reports, so an edit takes
effect on the next report without a restart. With no tags saved, it SHALL send an empty list. The agent SHALL NOT be
asked for, or shown, personal tags.

#### Scenario: Next report picks up an edit
- **WHEN** the bridge is running, and the member runs `panopticon tags add urgent`
- **THEN** the agent's next report carries `urgent`

#### Scenario: Tags removed
- **WHEN** the member removes every personal tag
- **THEN** the next report carries an empty list, and the change loses that agent's tags

### Requirement: Tagging through the bridge
The bridge's `tag_change` SHALL forward to the server's `tag_change` with the same server and token as reports (see
Configuration). When the agent gives no Project id or repo, the bridge SHALL fill them from `.panopticon.json` and the
git remote, as for reports. Errors SHALL come back as tool errors, and the bridge SHALL keep running.

#### Scenario: An agent tags its change
- **WHEN** an agent in a repo linked to `PAN` calls the bridge's `tag_change` with change `walking-skeleton` and add
  `bug`
- **THEN** the server tags `PAN`'s `walking-skeleton` with `bug`, and the agent gets the change's project tags

#### Scenario: No link file
- **WHEN** an agent calls `tag_change` in a repo with no `.panopticon.json` and gives no Project id
- **THEN** the tool returns an error saying the repo isn't linked to a Project, so there are no project tags to set

#### Scenario: Refused by the server
- **WHEN** the server refuses the call because the tag doesn't exist
- **THEN** the agent gets the server's error, listing the Project's tags, and the bridge keeps serving

### Requirement: Reported change recorded
After each report the server accepts, the bridge SHALL record the reported change's name for its working tree, in a
file on the machine outside the repo, keyed by the working tree's root, so `panopticon hook` can find it (see
`agent-presence`, A session's change). An archived report SHALL clear the record when it still names that change. A
failure to write the record SHALL NOT fail the report.

#### Scenario: Recorded
- **WHEN** the bridge reports `team-auth` from `~/src/panopticon` and the server accepts it
- **THEN** a hook run in `~/src/panopticon` finds `team-auth`

#### Scenario: Two worktrees
- **WHEN** one worktree of a repo reports `tags` and another reports `projects`
- **THEN** a hook in each worktree finds its own change

#### Scenario: Archived
- **WHEN** the bridge reports `team-auth` archived, and the record still names `team-auth`
- **THEN** a hook in that working tree finds no recorded change

#### Scenario: Refused report
- **WHEN** the server refuses a report of `tags`
- **THEN** the record keeps the change it named before
