# Spec Delta

## ADDED Requirements

### Requirement: Sessions on a card
A card SHALL list its change's current sessions (see `agent-presence`). Each SHALL show either its activity with how
long ago it started, or what it is waiting on with how long it has waited, in text, not by colour or icon alone. Its
member and machine, and the full time, SHALL be in the elapsed time's tooltip, and in the text a screen reader
reads. Sessions SHALL update in place as their events arrive, without a reload.

#### Scenario: Running gates
- **WHEN** Emmz's session on `tags` starts Bash with the description "Run make gates"
- **THEN** within a few seconds the `tags` card shows "Run make gates, just now", with "Emmz on mbp" and the time in
  the tooltip of "just now"

#### Scenario: Waiting since lunch
- **WHEN** a session's turn ended 2 hours ago
- **THEN** its card shows it "waiting for you, 2h"

### Requirement: Sessions don't reorder cards
Session events SHALL NOT change the order of cards, which stays by last report (see One card per change).

#### Scenario: Busy older change
- **WHEN** the `tags` card is second, and a session on `tags` starts a tool
- **THEN** the `tags` card stays second

### Requirement: Needs you
A card with a session waiting on a question or permission prompt SHALL say so. To a viewer who may answer it (see
`remote-answers`, Who may answer), it SHALL say "Needs you" and offer the prompt. To anyone else, it SHALL name the
member it needs. A session waiting after its turn ended SHALL NOT count as needing anyone.

#### Scenario: Owner's view
- **WHEN** Emmz's session holds a question while she is away
- **THEN** her dashboard shows "Needs you" on that card, with the question's form

#### Scenario: Not away
- **WHEN** Emmz isn't away and her session asks a question
- **THEN** her card shows "Needs you", with the question and "answer in the terminal", and no form

#### Scenario: A teammate's view
- **WHEN** Sam, an editor of `PAN`, views the same card
- **THEN** it shows "Needs Emmz: question", with no form and no question text

### Requirement: Away switch in the header
The signed-in header SHALL show the member's away switch, saying in text whether they are away. Turning it on or off
there SHALL work as `panopticon away` does (see `remote-answers`, Away mode).

#### Scenario: Turning away on
- **WHEN** Emmz turns the switch on in the header
- **THEN** it reads "Away", and her sessions' next prompts are held for the dashboard

### Requirement: Needs-you count in the tab title
While any card has a held prompt (see `remote-answers`, A prompt while away) that the viewer may answer, the page's
title SHALL begin with the count of such prompts in brackets, e.g. "(2) PANOPTICON". It SHALL drop the count when there are none.

#### Scenario: Two prompts
- **WHEN** Emmz is away and two of her sessions each hold a question
- **THEN** her dashboard's tab title is "(2) PANOPTICON"

### Requirement: Browser notifications
The dashboard SHALL offer to turn on browser notifications, and SHALL ask the browser's permission only when the
member chooses to. When on, a newly held prompt the viewer may answer SHALL raise one notification naming the change, and
selecting it SHALL bring the dashboard to that card.

#### Scenario: Notification
- **WHEN** notifications are on and Emmz's session on `tags` starts holding a question
- **THEN** her browser shows one notification for `tags`

#### Scenario: Never asked
- **WHEN** a member has never chosen to turn notifications on
- **THEN** the browser never asks them for notification permission

## MODIFIED Requirements

### Requirement: Quiet cards
A card SHALL be active until its change's last activity is older than the idle threshold, idle until it is older than
the stale threshold, and stale after that. A change's last activity is its last report or its sessions' last event
(see `agent-presence`), whichever is later. Each state SHALL look distinct and be named in text, not by colour alone.
A stale card SHALL say when it was last seen. A card SHALL stay on the dashboard whatever its state.

#### Scenario: Recently reported
- **WHEN** a change was last reported 5 minutes ago
- **THEN** its card is active

#### Scenario: Quiet for an hour
- **WHEN** a change was last reported an hour ago, with the default thresholds
- **THEN** its card is shown as idle

#### Scenario: Quiet for a morning
- **WHEN** a change was last reported 5 hours ago, with the default thresholds
- **THEN** its card is shown as stale, dimmed, saying "last seen 5h ago"

#### Scenario: Running gates for an hour
- **WHEN** a change was last reported an hour ago, and one of its sessions started a tool a minute ago
- **THEN** its card is active

### Requirement: Quiet state moves on by itself
A card's state SHALL follow the clock: an open dashboard SHALL move a card from active to idle to stale as the
thresholds pass, without a report or a reload. A new report on the change, or a new event from one of its sessions,
SHALL make its card active again at once.

#### Scenario: Goes idle while watching
- **WHEN** a dashboard is open and a change's last report passes 30 minutes old, with the default thresholds
- **THEN** its card is shown as idle within a minute, with no reload

#### Scenario: Report wakes a stale card
- **WHEN** a stale card's change is reported again
- **THEN** its card is active as soon as the update arrives

#### Scenario: Session wakes an idle card
- **WHEN** an idle card's session starts a tool
- **THEN** its card is active as soon as the event arrives
