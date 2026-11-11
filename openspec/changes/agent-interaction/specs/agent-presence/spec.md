# Spec Delta

## Purpose

Shows what each Claude Code session working on a change is doing right now, and whether it is waiting for a person,
from the Claude Code plugin's hooks rather than from the agent's own reports.

## ADDED Requirements

### Requirement: Session events from hooks
`panopticon hook` SHALL read one Claude Code hook event as JSON from stdin and send the session's new state to the
server with the same server and token as reports (see `cli-bridge`, Configuration). It SHALL work for the
`PreToolUse`, `PostToolUse`, `PermissionRequest`, `Stop`, `UserPromptSubmit` and `SessionEnd` events, and ignore
any other event.

#### Scenario: A tool starts
- **WHEN** Claude Code runs the hook for `PreToolUse` on a Bash call in a session working on `team-auth`
- **THEN** the server holds that session's new activity, and dashboards showing `team-auth` show it

#### Scenario: An event it doesn't use
- **WHEN** the hook is run with a `PreCompact` event
- **THEN** it sends nothing and exits 0 with no output

### Requirement: Hooks never break the session
Apart from a prompt waiting for an answer (see `remote-answers`), `panopticon hook` SHALL finish within 5 seconds and
SHALL exit 0 with no output, whatever happens. That includes a missing login, an unreachable server, a refusal and
unreadable input. It SHALL write a failure to the CLI's log, never to stdout, and SHALL NOT print the token.

#### Scenario: Server down
- **WHEN** the server can't be reached and a `PreToolUse` hook runs
- **THEN** the hook exits 0 within 5 seconds with no output, and the tool runs as usual

#### Scenario: Not logged in
- **WHEN** the machine has no saved login and no `PANOPTICON_TOKEN`
- **THEN** every hook exits 0 with no output, and nothing is sent

### Requirement: A session's change
A session event SHALL name the change the session is working on: the change the bridge last reported for the
session's working tree (see `cli-bridge`, Reported change recorded), or else the change in the branch name
`change/<name>`. The event SHALL carry the repo identity and Project link as reports do. With neither change found,
the hook SHALL send nothing.

#### Scenario: Change from the bridge
- **WHEN** the bridge last reported `team-auth` from this working tree, and the branch is `main`
- **THEN** the session's events name `team-auth`

#### Scenario: Change from the branch
- **WHEN** nothing has been reported from this working tree and the branch is `change/tags`
- **THEN** the session's events name `tags`

#### Scenario: No change at all
- **WHEN** nothing has been reported from this working tree and the branch is `main`
- **THEN** the hook sends nothing

### Requirement: Session identity
The server SHALL identify a session by its Claude Code session id together with the reporting member, who SHALL come
from the token, never from the event. A session SHALL belong to the active change its events name. An event for a
change the server has no active card for SHALL be accepted and SHALL show nowhere.

#### Scenario: Two sessions on one change
- **WHEN** two Claude Code sessions of Emmz on the same machine both work on `team-auth`
- **THEN** the card shows two sessions, each with its own activity

#### Scenario: Change not reported yet
- **WHEN** a session's events name `tags`, and no report for `tags` has been accepted
- **THEN** no card shows the session, and its events change no card

### Requirement: Activity text
A session's activity SHALL be one line of at most 120 characters: the Bash tool's `description` when it has one, and
otherwise a summary naming the tool and its target, such as "Editing tasks.md" or "Running agent: Review the diff".
It SHALL NOT include a Bash command, a file's content, or any other tool input beyond a file's name.

#### Scenario: Bash with a description
- **WHEN** a session runs Bash with the description "Run make gates" and the command `make gates`
- **THEN** its activity is "Run make gates"

#### Scenario: Bash without a description
- **WHEN** a session runs Bash with no description
- **THEN** its activity is "Running a command", and the command itself is never sent

#### Scenario: An edit
- **WHEN** a session edits `openspec/changes/tags/tasks.md`
- **THEN** its activity is "Editing tasks.md"

### Requirement: Session states
A session SHALL be working, with its activity, or waiting. Waiting is on a question, on a permission prompt, or on
the user after its turn ended. `PreToolUse` SHALL set working, except on `AskUserQuestion`, which sets waiting on a
question. `PermissionRequest` SHALL set waiting on a permission prompt, except on `AskUserQuestion`, which it SHALL
ignore. `Stop` SHALL set waiting on the user.
`PostToolUse` and `UserPromptSubmit` SHALL set working. `SessionEnd` SHALL remove the session.

#### Scenario: Turn ends
- **WHEN** a session's `Stop` hook runs
- **THEN** its card shows it waiting for the user, with how long it has waited

#### Scenario: A question is not a permission prompt
- **WHEN** a session asks with `AskUserQuestion` and Claude Code also raises a `PermissionRequest` for it
- **THEN** its card shows it waiting on a question, once, and never on a permission prompt

#### Scenario: Approved in the terminal
- **WHEN** a session waiting on a permission prompt for Bash is allowed in the terminal, and the command finishes
- **THEN** its card shows it working again, not waiting

#### Scenario: Back to work
- **WHEN** a waiting session's user submits a prompt
- **THEN** its card shows it working again

#### Scenario: Session closed
- **WHEN** a session's `SessionEnd` hook runs
- **THEN** the session leaves its card

### Requirement: Presence is not history
Session state SHALL be held by the server only while it is current. It SHALL NOT be recorded as a report event, and
SHALL NOT take part in history, metrics or task progress. A session SHALL expire when no event has arrived for the
stale threshold (see `live-dashboard`, Quiet thresholds are settings), apart from a session holding a prompt (see
A held prompt keeps its session). A server restart MAY lose every session, and a session's next event SHALL bring
it back.

#### Scenario: Forgotten session
- **WHEN** a session's last event was 5 hours ago, with the default thresholds
- **THEN** it is no longer shown on its card

#### Scenario: Metrics unaffected
- **WHEN** a change has had 400 session events and 6 reports
- **THEN** its history and metrics are the same as with the 6 reports alone

### Requirement: A held prompt keeps its session
A session holding a prompt (see `remote-answers`, A prompt while away) SHALL NOT expire, however long the prompt waits.
Once the prompt is answered or released, the stale threshold SHALL count from that moment.

#### Scenario: Away overnight
- **WHEN** Emmz is away and her session has held a question for 5 hours, with the default thresholds
- **THEN** the session and its question are still on its card, and she can still answer it

#### Scenario: Released, then quiet
- **WHEN** a held prompt is released after 6 hours, and the session sends no event for 4 hours more
- **THEN** the session is no longer shown on its card

### Requirement: Who sees a session
A session SHALL be visible to exactly the members who can see its change, through the dashboard's reads and its live
stream. The member it belongs to and its machine SHALL be shown with it.

#### Scenario: Outsider
- **WHEN** a session works on a `PAN` change, and Sam isn't a member of `PAN`
- **THEN** none of Sam's responses or live events mention the session
