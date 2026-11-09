# Spec Delta

## MODIFIED Requirements

### Requirement: Report content
A `report_progress` call SHALL carry the repo identity (host, owner, repo name), the change name, the
reporting agent (client name and machine name), optionally the current section number, and the content
of the change's `tasks.md`. It MAY also carry the change's documents, each a relative path and its content, and
an archive folder name when the change has been archived.

#### Scenario: Valid report accepted
- **WHEN** an authenticated client calls `report_progress` with all required fields
- **THEN** the tool returns success, including the computed overall and current-section progress

#### Scenario: Missing required field
- **WHEN** a call omits the repo identity, the change name or `tasks.md`
- **THEN** the tool returns an error naming the missing field and records nothing

#### Scenario: Report from an older CLI
- **WHEN** a report carries `tasks.md` but no documents
- **THEN** it is accepted, and the change's document list holds only its tasks

### Requirement: Change identity
The server SHALL identify an active change by repo host, owner, repo name and change name, compared without
regard to case. Reports with the same identity SHALL update the same active change. Archived changes SHALL NOT
take part: a report for a name whose change is archived starts a new active change, unless it is itself an
archived report for that change.

#### Scenario: Repeat report updates the same change
- **WHEN** two reports arrive for `github.com/emmz/panopticon` change `walking-skeleton`
- **THEN** the server holds one change, with the second report's progress

#### Scenario: Case-insensitive identity
- **WHEN** one report names owner `Emmz` and a later one names owner `emmz` for the same repo and change
- **THEN** both reports update the same change

#### Scenario: Name reused after archive
- **WHEN** `cleanup` in `emmz/panopticon` was archived, and a new, unarchived `cleanup` change is then reported
- **THEN** the server holds a new active change `cleanup`, and the archived one is unchanged in history

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

## ADDED Requirements

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
