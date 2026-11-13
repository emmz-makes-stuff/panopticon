## MODIFIED Requirements

### Requirement: Session states
A session SHALL be working, with its activity, or waiting. Waiting is on a question, on a permission prompt, on a visit
request, or on the user after its turn ended. `PreToolUse` SHALL set working, except on `AskUserQuestion`, which sets
waiting on a question, and on the bridge's `request_visit`, which sets waiting on a visit request. `PermissionRequest`
SHALL set waiting on a permission prompt, except on `AskUserQuestion`, which it SHALL ignore. `Stop` SHALL set waiting
on the user.
`PostToolUse` and `UserPromptSubmit` SHALL set working. `SessionEnd` SHALL remove the session.

#### Scenario: Turn ends
- **WHEN** a session's `Stop` hook runs
- **THEN** its card shows it waiting for the user, with how long it has waited

#### Scenario: A question is not a permission prompt
- **WHEN** a session asks with `AskUserQuestion` and Claude Code also raises a `PermissionRequest` for it
- **THEN** its card shows it waiting on a question, once, and never on a permission prompt

#### Scenario: A visit request
- **WHEN** a session calls `request_visit` while its member is away
- **THEN** its card shows it waiting on a visit request, once

#### Scenario: Approved in the terminal
- **WHEN** a session waiting on a permission prompt for Bash is allowed in the terminal, and the command finishes
- **THEN** its card shows it working again, not waiting

#### Scenario: Back to work
- **WHEN** a waiting session's user submits a prompt
- **THEN** its card shows it working again

#### Scenario: Session closed
- **WHEN** a session's `SessionEnd` hook runs
- **THEN** the session leaves its card
