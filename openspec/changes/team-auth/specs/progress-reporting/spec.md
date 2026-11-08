# Spec Delta

## MODIFIED Requirements

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
