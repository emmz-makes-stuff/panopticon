# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
