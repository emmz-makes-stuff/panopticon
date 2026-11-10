# change-history Specification

## Purpose

What becomes of a change once it is archived in OpenSpec: it leaves the live view and joins a browsable history,
kept as its final snapshot of documents, agents and progress.

## Requirements

### Requirement: History list
A signed-in member SHALL be able to list the archived changes they can see (spec change-visibility), the most
recently archived first. Each one SHALL show its repo (host, owner and name), change name, its Project's code (or
that it's personal), when it was archived, and the agents that worked on it with their members.

#### Scenario: Two archived changes
- **WHEN** `walking-skeleton` was archived on 2026-11-07 and `team-auth` on 2026-11-08
- **THEN** the history lists `team-auth` first, then `walking-skeleton`, each with its repo, archive date and agents

#### Scenario: Nothing archived yet
- **WHEN** no change has been archived
- **THEN** the history shows an empty state saying archived changes will appear there

#### Scenario: Signed out
- **WHEN** someone without a session opens the history
- **THEN** the API answers 401, and the app shows the sign-in page and then returns to the history

#### Scenario: History row shows its Project
- **WHEN** `team-auth` was archived in Project `PAN`
- **THEN** its history row shows `PAN`

### Requirement: Retired copies in the history
A personal change that was retired because its repo was linked to a Project that already had the change (spec
change-visibility, Linking when the Project already has the change) SHALL stay in its owner's history, shown as
linked to that Project in place of an archive folder.

#### Scenario: A copy retired by linking
- **WHEN** Emmz's personal `cleanup` was retired because `PAN` already had `cleanup` when she linked the repo
- **THEN** her history lists it as "Linked to PAN", with no archive folder

### Requirement: Final snapshot
An archived change SHALL keep the documents, progress and agents of its archived report, which is its final
state. Opening it from the history SHALL show its document list and documents as they were when it was archived.

#### Scenario: Open an archived change
- **WHEN** a member opens `team-auth` from the history
- **THEN** it shows the documents synced from `archive/2026-11-08-team-auth/`, with its final progress

#### Scenario: Later reports don't change it
- **WHEN** an agent reports change `team-auth` after it was archived, and no active `team-auth` folder exists
- **THEN** the archived snapshot is unchanged

### Requirement: Reports kept for later
Every accepted report SHALL keep its manifest: each synced document's path and content hash. This lets a later
change show the change as it was at any report without new storage.

#### Scenario: Manifest per report
- **WHEN** three reports on a change synced different versions of its design
- **THEN** each report's manifest names the design with the hash of the version that report carried
