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
of the change's `tasks.md`.

#### Scenario: Valid report accepted
- **WHEN** an authenticated client calls `report_progress` with all required fields
- **THEN** the tool returns success, including the computed overall and current-section progress

#### Scenario: Missing required field
- **WHEN** a call omits the repo identity, the change name or `tasks.md`
- **THEN** the tool returns an error naming the missing field and records nothing

### Requirement: Change identity
The server SHALL identify a change by repo host, owner, repo name and change name, compared without
regard to case. Reports with the same identity SHALL update the same change.

#### Scenario: Repeat report updates the same change
- **WHEN** two reports arrive for `github.com/emmz/panopticon` change `walking-skeleton`
- **THEN** the server holds one change, with the second report's progress

#### Scenario: Case-insensitive identity
- **WHEN** one report names owner `Emmz` and a later one names owner `emmz` for the same repo and change
- **THEN** both reports update the same change

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
The server SHALL store the reported `tasks.md` content in object storage, and SHALL record every report
as an event that references the stored content, so later changes can build history and metrics on it.

#### Scenario: Report kept as an event
- **WHEN** a report is accepted
- **THEN** a report event exists with the change, agent, section, timestamp and a reference to the stored
  `tasks.md`

#### Scenario: Unchanged document stored once
- **WHEN** two reports carry byte-identical `tasks.md` content
- **THEN** object storage holds that content once and both events reference it

### Requirement: Change updates published
Each accepted report SHALL publish the change's new state to the live-update stream (see
`live-dashboard`).

#### Scenario: Report triggers an update
- **WHEN** a report is accepted while a dashboard is connected
- **THEN** the dashboard receives the change's updated state
