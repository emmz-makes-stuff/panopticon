# Spec Delta

## Purpose

Lets a member answer their Claude Code sessions' questions and permission prompts from the dashboard while they are
away from the terminal, with clear rules on who may answer and a record of every answer given.

## ADDED Requirements

### Requirement: Away mode
Each member SHALL have one away switch, off by default. They SHALL be able to turn it on and off from the dashboard
and with `panopticon away on`, `panopticon away off` and `panopticon away` (which prints its state). It SHALL apply
to every session reporting with that member's tokens. It SHALL survive a server restart, and a change SHALL reach the
member's open dashboards without a reload.

#### Scenario: Away from the CLI
- **WHEN** Emmz runs `panopticon away on`
- **THEN** her open dashboards show her as away, and `panopticon away` prints that she is away

#### Scenario: Restart
- **WHEN** Emmz is away and the server restarts
- **THEN** she is still away

### Requirement: A question while not away
When a session asks with `AskUserQuestion` and its member isn't away, the hook SHALL let the terminal ask as usual.
It SHALL show the question on the session's card. Answering it in the terminal, or the session moving on, SHALL
clear it from the card.

#### Scenario: At the keyboard
- **WHEN** Emmz isn't away and her session asks a question
- **THEN** the terminal shows the question at once, and her card shows it with "answer in the terminal"

### Requirement: A prompt while away
When a session's member is away and it asks with `AskUserQuestion` or meets a permission prompt, the hook SHALL hold
the prompt until it is answered from the dashboard or released. The terminal SHALL show nothing until then. A prompt
from a session that has no card, or that breaks the prompt limits, SHALL go to the terminal at once.

#### Scenario: Answered on the dashboard
- **WHEN** Emmz is away, her session asks "Which store?" with options "Memory" and "Postgres", and she picks
  "Memory" on the dashboard
- **THEN** the session carries on with "Memory" as the answer, and the terminal never shows the question

#### Scenario: No card
- **WHEN** Emmz is away and a session whose change has no card asks a question
- **THEN** the terminal shows the question at once

### Requirement: Releasing a prompt
A held prompt SHALL go back to the terminal, unanswered, when its member turns away mode off, when it has waited 8
hours, or when the hook loses the server for over a minute. Once released, it SHALL NOT be answerable from the
dashboard.

#### Scenario: Back at the desk
- **WHEN** a prompt is held and Emmz runs `panopticon away off`
- **THEN** the terminal shows the prompt within a few seconds, and the card no longer offers to answer it

#### Scenario: Server goes away
- **WHEN** a prompt is held and the server stops answering
- **THEN** the terminal shows the prompt within about a minute

### Requirement: Question forms
The dashboard SHALL render a question as the agent asked it: up to four questions, each with a header, its options'
labels, descriptions and previews, single or multiple choice, and a free-text "Other" answer. The session SHALL get
the chosen labels (joined by commas for multiple choice) or the free text, as if answered in the terminal.

#### Scenario: Multiple choice
- **WHEN** a held question allows several answers and Bob picks "Hooks" and "Web"
- **THEN** the session's answer is "Hooks, Web"

#### Scenario: Other
- **WHEN** Emmz types "Neither, use SQLite" as the Other answer
- **THEN** the session's answer is "Neither, use SQLite"

#### Scenario: Yes or no
- **WHEN** a held question has the options "Yes" and "No"
- **THEN** the dashboard shows it as two buttons and an Other answer

### Requirement: Permission prompts
The dashboard SHALL show a held permission prompt with the tool's name and a preview of its input, and SHALL offer
Allow and Deny, with an optional message for Deny. Allow SHALL let the tool run once, never adding a permission rule.
Deny SHALL stop the tool and give the session the message.

#### Scenario: Deny with a reason
- **WHEN** Emmz denies a held prompt to run `make e2e` with "not now, the image is stale"
- **THEN** the tool doesn't run, and the session is told it was denied with that message

#### Scenario: Allow once
- **WHEN** Emmz allows a held prompt for a Bash command
- **THEN** the command runs, and the next run of that same command prompts again

### Requirement: Permission input stays local unless held
A permission prompt's tool input SHALL leave the machine only after the server has said the prompt is held, and then
only as a preview cut to 2 KB. While its member isn't away, only the tool's name SHALL be sent. The dashboard SHALL
offer Allow only once the preview has arrived.

#### Scenario: Not away
- **WHEN** Emmz isn't away and her session meets a permission prompt for `curl -H "Authorization: Bearer abc" …`
- **THEN** the server receives only that it's a permission prompt for Bash, and her card shows "waiting on permission
  for Bash · answer in the terminal"

#### Scenario: Away
- **WHEN** Emmz is away and her session meets the same prompt
- **THEN** the preview is sent after the server holds the prompt, and Allow is offered once it has arrived

### Requirement: Who may answer
A held prompt SHALL be answerable by the member its session belongs to, and by the owners of its change's Project. A
personal change's prompts SHALL be answerable only by its member. The server SHALL refuse anyone else, and SHALL
accept only the first answer, refusing later ones as already answered.

#### Scenario: Project owner answers
- **WHEN** Bob owns `PAN`, and Emmz's session on a `PAN` change holds a prompt
- **THEN** Bob can answer it

#### Scenario: Editor can't
- **WHEN** Sam is an editor of `PAN` and tries to answer the same prompt
- **THEN** the server refuses, and the prompt is still held

#### Scenario: Two answers at once
- **WHEN** Emmz and Bob answer the same prompt at the same moment
- **THEN** exactly one answer reaches the session, and the other is told it was already answered

### Requirement: What others see of a prompt
A prompt's questions, options, previews and tool input SHALL be shown only to members who may answer it. Every other
member who can see the change SHALL see only that the session needs its member, and whether for a question or a
permission prompt.

#### Scenario: A viewer
- **WHEN** Emmz's session holds a permission prompt for Bash, and Sam (an editor of `PAN`) views its card
- **THEN** Sam sees "Needs Emmz: permission for Bash", and no part of the command reaches Sam's browser

### Requirement: Answers are attributed
Every answer given from the dashboard SHALL be recorded with its change, the session's member, who answered, when,
and its kind: a question, with its question texts and the chosen labels, or a permission prompt, with the tool's name
and Allow or Deny. The session SHALL be told who answered, via Panopticon. Previews and tool input SHALL NOT be recorded.

#### Scenario: Bob answers Emmz's question
- **WHEN** Bob answers a question held by Emmz's session
- **THEN** the session is told "answered by Bob via Panopticon", and the change records Bob's answer

### Requirement: Prompt limits
A prompt SHALL be sent to the dashboard only when it has at most four questions, each with at most four options, every
preview is at most 8 KB, and the whole prompt is at most 64 KB. A permission prompt's input preview SHALL be cut to
2 KB. Otherwise the prompt SHALL go to the terminal as usual.

#### Scenario: Huge preview
- **WHEN** Emmz is away and a question has a 20 KB preview
- **THEN** the terminal shows the question, and the card shows the session waiting on a question it can't show

### Requirement: Safe rendering
Option previews and permission input SHALL be shown as the dashboard shows documents (see `change-documents`,
Safe rendering), with no raw HTML and no unsafe links.

#### Scenario: Script in a preview
- **WHEN** a preview contains `<script>alert(1)</script>`
- **THEN** the dashboard shows it as text and runs nothing
