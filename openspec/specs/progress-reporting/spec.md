# progress-reporting Specification

## Purpose

The server's online MCP endpoint. Agents, through the CLI bridge, report which OpenSpec change they are
working on, and the server keeps the latest state of each change for the dashboard.

## Requirements

### Requirement: Online MCP endpoint
The server SHALL expose an MCP endpoint over streamable HTTP that offers a `report_progress` tool.

#### Scenario: Tool is discoverable
- **WHEN** an MCP client with a valid token lists the endpoint's tools
- **THEN** the list includes `report_progress` with its input schema

### Requirement: Bearer token required
The server SHALL reject any MCP request that doesn't carry a valid, unrevoked API token of an active member.
The token check SHALL compare hashes, never stored secrets. The shared bootstrap token SHALL be accepted only
in the Development environment, and the server SHALL NOT start elsewhere when one is configured.

#### Scenario: Missing token
- **WHEN** a request reaches the MCP endpoint without an `Authorization` header
- **THEN** the server responds 401 and records nothing

#### Scenario: Wrong token
- **WHEN** a request carries a bearer token that isn't an active API token
- **THEN** the server responds 401 and records nothing

#### Scenario: No token configured
- **WHEN** the server starts with no bootstrap token configured
- **THEN** it starts normally, and reports need a member's API token

#### Scenario: Revoked token
- **WHEN** a request carries a token its member has revoked, or a token of a removed member
- **THEN** the server responds 401 and records nothing

#### Scenario: Bootstrap token in Development
- **WHEN** the server runs in Development with `Panopticon:BootstrapToken` set, and a report carries it
- **THEN** the report is accepted and recorded with no member

#### Scenario: Bootstrap token elsewhere
- **WHEN** the server starts outside Development with `Panopticon:BootstrapToken` set
- **THEN** start-up fails with an error naming the setting

### Requirement: Report content
A `report_progress` call SHALL carry the repo identity (host, owner, repo name), the change name, the
reporting agent (client name and machine name), optionally the current section number, and the content
of the change's `tasks.md`. It MAY also carry the change's documents, each a relative path and its content, an
archive folder name when the change has been archived, and the id of the Project the work is linked to.

#### Scenario: Valid report accepted
- **WHEN** an authenticated client calls `report_progress` with all required fields
- **THEN** the tool returns success, including the computed overall and current-section progress

#### Scenario: Missing required field
- **WHEN** a call omits the repo identity, the change name or `tasks.md`
- **THEN** the tool returns an error naming the missing field and records nothing

#### Scenario: Report from an older CLI
- **WHEN** a report carries `tasks.md` but no documents
- **THEN** it is accepted, and the change's document list holds only its tasks

#### Scenario: Report from a CLI without Projects
- **WHEN** a report carries no Project id
- **THEN** it is accepted into the reporter's personal scope (spec change-visibility)

### Requirement: Change identity
The server SHALL identify an active change by its scope (its Project, or the reporting member when it names no
Project), repo host, owner, repo name and change name, the names compared without regard to case. Reports with the
same identity SHALL update the same active change. Archived changes SHALL NOT take part: a report for a name whose
change is archived starts a new active change, unless it is itself an archived report for that change.

#### Scenario: Repeat report updates the same change
- **WHEN** two reports arrive for `github.com/emmz/panopticon` change `walking-skeleton`, both naming `PAN`
- **THEN** the server holds one change, with the second report's progress

#### Scenario: Case-insensitive identity
- **WHEN** one report names owner `Emmz` and a later one names owner `emmz` for the same repo, change and scope
- **THEN** both reports update the same change

#### Scenario: Name reused after archive
- **WHEN** `cleanup` in `emmz/panopticon` was archived, and a new, unarchived `cleanup` change is then reported
- **THEN** the server holds a new active change `cleanup`, and the archived one is unchanged in history

#### Scenario: Two scopes, two changes
- **WHEN** Emmz reports `cleanup` in `emmz/panopticon` naming `PAN`, and Kit reports the same repo and change with
  no Project
- **THEN** the server holds two changes, one in `PAN` and one in Kit's personal scope, and neither updates the other

#### Scenario: Linked after it started
- **WHEN** Emmz's report naming `PAN` arrives for a change she has been reporting with no Project
- **THEN** that change moves into `PAN` (spec change-visibility, Linking moves personal work), and no second change is
  created

#### Scenario: Two people, no Project
- **WHEN** Emmz and Kit each report `scratch` in the same repo with no Project
- **THEN** each has their own change, seen only by them

### Requirement: Agents on a change
The server SHALL record each distinct reporting agent on a change, identified by its member, client name and
machine name, with when that agent last reported. The member SHALL come from the token, never from the
report.

#### Scenario: Two agents on one change
- **WHEN** agents `claude-code@vm-1` and `cursor@vm-2` both report on the same change
- **THEN** the change lists both agents, each with its own last-report time

#### Scenario: Two members on one machine
- **WHEN** Emmz and Sam each report from `claude-code` on `vm-1` with their own tokens
- **THEN** the change lists two agents, one for each member

### Requirement: Document retention
The server SHALL store each reported document's content in object storage once per content hash, and SHALL record
every report as an event that references the stored `tasks.md` and the report's manifest of document paths and
hashes, so later changes can build history and metrics on it.

#### Scenario: Report kept as an event
- **WHEN** a report is accepted
- **THEN** a report event exists with the change, agent, section, timestamp and a reference to the stored
  `tasks.md`

#### Scenario: Unchanged document stored once
- **WHEN** two reports carry byte-identical `tasks.md` content
- **THEN** object storage holds that content once and both events reference it

#### Scenario: Unchanged design stored once
- **WHEN** two reports carry the same `design.md` and different `tasks.md`
- **THEN** object storage holds that design once, and both reports' manifests reference it

### Requirement: Change updates published
Each accepted report SHALL publish the change's new state to the live-update stream (see
`live-dashboard`).

#### Scenario: Report triggers an update
- **WHEN** a report is accepted while a dashboard is connected
- **THEN** the dashboard receives the change's updated state

### Requirement: Archived report
A report carrying an archive folder name SHALL mark its active change archived, with the time and the folder name,
after recording its documents and progress as its final state. A report for a change that is already archived
under that folder name SHALL change nothing and SHALL return that change's final progress.

#### Scenario: Change archived
- **WHEN** an archived report for `team-auth` with folder `2026-11-08-team-auth` is accepted
- **THEN** the change is archived as `2026-11-08-team-auth` with its final documents, and leaves the live view

#### Scenario: Archived twice
- **WHEN** the watcher and the agent both send the archived report for `team-auth`
- **THEN** the second changes nothing and returns the same final progress

#### Scenario: Archived with no active change
- **WHEN** an archived report arrives for a change the server has never seen
- **THEN** the server records it as an archived change straight away, so it appears in history

### Requirement: Document checks
The server SHALL reject a report whose document paths are not plain relative paths inside the change's folder,
or whose documents exceed 256 KB each (1 MiB for `tasks.md`, per `task-progress`), 4 MB in total, or 500 in number, with an error naming the problem, and SHALL record nothing.

#### Scenario: Path escapes the folder
- **WHEN** a report carries a document at `../../secrets.md`
- **THEN** the tool returns an error naming that path, and records nothing

#### Scenario: Oversized report
- **WHEN** a report's documents total over 4 MB
- **THEN** the tool returns an error naming the limit, and records nothing

#### Scenario: Too many documents
- **WHEN** a report carries 501 documents, each of them empty
- **THEN** the tool returns an error naming the 500-document limit, and records nothing

### Requirement: Reporting into a Project
A report naming a Project SHALL be accepted only when that Project exists and the reporting member belongs to it, in
any role. Otherwise the tool SHALL return an error saying the Project wasn't found or they aren't a member, naming
the id and pointing to `list_projects`, and SHALL record nothing. A malformed id SHALL be rejected the same way.

#### Scenario: Not a member
- **WHEN** Sam, who isn't a member of `ACME`, reports with `ACME`'s id
- **THEN** the tool returns an error naming that id and `list_projects`, and nothing is recorded

#### Scenario: Unknown Project
- **WHEN** a report names an id that no Project has
- **THEN** the tool returns the same error, and nothing is recorded

#### Scenario: A viewer reports
- **WHEN** Kit, a viewer of `PAN`, reports with `PAN`'s id
- **THEN** the report is accepted into `PAN`
