# Spec Delta

## MODIFIED Requirements

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
