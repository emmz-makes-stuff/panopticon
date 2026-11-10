## MODIFIED Requirements

### Requirement: Report content
A `report_progress` call SHALL carry the repo identity (host, owner, repo name), the change name, the
reporting agent (client name and machine name), optionally the current section number, and the content
of the change's `tasks.md`. It MAY also carry the change's documents, each a relative path and its content, an
archive folder name when the change has been archived, the id of the Project the work is linked to, and the
reporter's personal tags.

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

## ADDED Requirements

### Requirement: Personal tags in reports
A report's personal tags SHALL replace the reporting agent's personal tag set on the change (see `tags`, Personal
tags on a change). A report with no personal tags field SHALL leave the agent's set as it was. A report with more than
20 personal tags, or a malformed one, SHALL be refused with an error naming the problem, and SHALL record nothing.

#### Scenario: Tags replace the agent's set
- **WHEN** `claude-code@vm-1` reported `dev` on a change, and reports again with `language: csharp`
- **THEN** that agent's set on the change is `language: csharp`

#### Scenario: Report from a CLI without tags
- **WHEN** an agent with a set `dev` sends a report with no personal tags field
- **THEN** the report is accepted, and the agent's set stays `dev`

#### Scenario: An empty list
- **WHEN** an agent with a set `dev` reports with an empty personal tags list
- **THEN** the agent's set on the change is empty

#### Scenario: Malformed tag refused
- **WHEN** a report's personal tags include `has space`
- **THEN** the tool returns an error naming `has space`, and nothing is recorded

#### Scenario: Too many
- **WHEN** a report carries 21 personal tags
- **THEN** the tool returns an error saying at most 20 are allowed, and nothing is recorded
