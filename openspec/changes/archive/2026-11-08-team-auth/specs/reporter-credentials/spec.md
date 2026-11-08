# Spec Delta

## Purpose

How a team member gives a reporter (the CLI on one machine) its own credential through a device-style login
approved in the dashboard, and how they see and revoke the API tokens issued that way.

## ADDED Requirements

### Requirement: Starting a device login
`panopticon login` SHALL ask the server for a device login on behalf of this machine. It SHALL show a short
code of 8 letters as `XXXX-XXXX` and the dashboard URL to approve it at, then wait. The login and its code
SHALL expire after 10 minutes.

#### Scenario: Login on a VM
- **WHEN** a member runs `panopticon login --server https://panopticon.example.com` on `vm-1`
- **THEN** the CLI prints a code such as `BCDF-GHJK` and `https://panopticon.example.com/device?code=BCDF-GHJK`,
  and waits for approval

#### Scenario: Nobody approves
- **WHEN** 10 minutes pass without an approval
- **THEN** the CLI stops with a message that the code expired and to run `panopticon login` again, and exits
  non-zero

### Requirement: Approving a device login
A signed-in member SHALL approve or deny a device login by its code, after the dashboard shows the machine
name the CLI sent. Opening the approval URL SHALL NOT approve anything by itself. Codes match ignoring case and
the hyphen.

#### Scenario: Approve
- **WHEN** a signed-in member opens the approval URL, sees `vm-1`, and approves
- **THEN** the waiting CLI receives an API token for that member, saves it, and says it's logged in as that
  member

#### Scenario: Deny
- **WHEN** the member denies the code
- **THEN** the CLI stops with a message that the login was denied, exits non-zero, and no token is issued

#### Scenario: Unknown or expired code
- **WHEN** a member enters a code that doesn't exist, has expired, or was already used
- **THEN** the dashboard says the code isn't valid, and nothing is approved

#### Scenario: Not signed in
- **WHEN** someone without a session opens the approval URL
- **THEN** they are asked to sign in first, and then come back to the same code

### Requirement: Device login secrets
The CLI SHALL wait for approval with a device secret that only it knows, separate from the code a person
types. The token SHALL be handed over once only, to that secret, and only after approval.

#### Scenario: Token handed over once
- **WHEN** the CLI has received its token and the same device secret asks again
- **THEN** no token is returned

#### Scenario: Polling too fast
- **WHEN** a client polls for the token more often than the server's stated interval
- **THEN** it is told to slow down, and its login is otherwise unaffected

### Requirement: API tokens
An API token SHALL belong to one member and one device login. It SHALL be a high-entropy secret, shown only
to the CLI that received it. The server SHALL store only a hash of it. A token SHALL stay valid until it is
revoked or its member is removed.

#### Scenario: Token never shown again
- **WHEN** a member looks at their tokens in the dashboard
- **THEN** no token's secret is shown

### Requirement: CLI saves its credential
On approval, `panopticon login` SHALL save the server URL and token to the user's Panopticon CLI config file.
That file SHALL be readable by the user only. Logging in again SHALL replace the saved server and token.

#### Scenario: Saved for the bridge
- **WHEN** `panopticon login` succeeds
- **THEN** the config file holds the server URL and token, with permissions for the user only, and
  `panopticon mcp` uses them without any environment variables

### Requirement: Tokens page
A signed-in member SHALL be able to list their own active tokens, with each one's machine name, when it was
created and when it was last used. They SHALL be able to revoke any of them. Members SHALL NOT see or revoke
other members' tokens.

#### Scenario: List and revoke
- **WHEN** a member revokes the token for `vm-1` on their tokens page
- **THEN** it disappears from the list, and the next report from `vm-1` is refused as a rejected token

#### Scenario: Someone else's token
- **WHEN** a member asks to revoke a token that belongs to another member
- **THEN** the request is refused as not found, and the token stays valid

#### Scenario: Last used
- **WHEN** a reporter reports with a token
- **THEN** that token's last-used time on the tokens page moves to about that time (to within a minute)
