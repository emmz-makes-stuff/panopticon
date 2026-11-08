# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: Document size limits
The bridge SHALL refuse to send a report whose documents include a file other than `tasks.md` over 256 KB, a
`tasks.md` over 1 MiB (as `task-progress` already limits it), or a total over 4 MB. The error SHALL name the offending file and its size, or the total, and nothing SHALL be sent.

#### Scenario: One file too large
- **WHEN** `devlog/huge.md` is 300 KB
- **THEN** `report_progress` returns an error naming `devlog/huge.md` and the 256 KB limit, and sends nothing

#### Scenario: Folder too large
- **WHEN** the change's documents total 5 MB, each under 256 KB
- **THEN** `report_progress` returns an error naming the 4 MB total limit, and sends nothing

### Requirement: Archived change reported
When the reported change has no folder under `openspec/changes/` but has one under `openspec/changes/archive/`
named `<YYYY-MM-DD>-<change>`, the bridge SHALL report that folder's documents, marked archived with the folder's
name. When there are several, it SHALL use the latest date.

#### Scenario: Report after archive
- **WHEN** `team-auth` was moved to `openspec/changes/archive/2026-11-08-team-auth/` and the agent reports
  `team-auth`
- **THEN** the bridge reports that folder's documents, marked archived as `2026-11-08-team-auth`, and returns the
  final progress

#### Scenario: Archived twice under one name
- **WHEN** both `archive/2026-03-01-cleanup/` and `archive/2026-11-08-cleanup/` exist and the agent reports
  `cleanup`
- **THEN** the bridge reports `archive/2026-11-08-cleanup/`

### Requirement: Watching for archives
While `panopticon mcp` runs, it SHALL watch `openspec/changes/` for the changes it has reported in this session.
When one of those folders moves to `openspec/changes/archive/`, the bridge SHALL report it archived by itself, as
the last agent that reported it, within a few seconds. A watcher failure SHALL be logged to stderr and SHALL NOT
stop the bridge.

#### Scenario: Archive while the bridge runs
- **WHEN** Claude Code reported `team-auth` through the bridge, then `/opsx:archive` moves its folder to `archive/`
- **THEN** the bridge reports `team-auth` archived, as `claude-code` on this machine, without the agent calling a
  tool

#### Scenario: Unreported change archived
- **WHEN** a change this bridge never reported on is moved to `archive/`
- **THEN** the bridge reports nothing about it

#### Scenario: Server down at archive time
- **WHEN** the watcher's archived report fails because the server can't be reached
- **THEN** the failure is logged to stderr, the bridge keeps serving, and the next `report_progress` for that
  change reports it archived
