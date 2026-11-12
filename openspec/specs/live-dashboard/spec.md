# live-dashboard Specification
## Purpose

The web dashboard's live view: one card per OpenSpec change that agents are reporting on, showing where
it stands at a glance and updating without a page refresh.
## Requirements

### Requirement: One card per change
The dashboard SHALL show one card for each active change the server knows about. Archived changes SHALL NOT have
a card: they are in the history (see `change-history`). The most recently reported change comes first.

#### Scenario: Two changes in two repos
- **WHEN** reports exist for `walking-skeleton` in `emmz/panopticon` and `add-auth` in `emmz/other`
- **THEN** the dashboard shows two cards, with the most recently reported one first

#### Scenario: No changes yet
- **WHEN** no report has ever been received
- **THEN** the dashboard shows an empty state explaining how to connect an agent

#### Scenario: Archived change has no card
- **WHEN** `walking-skeleton` is archived and `team-auth` is active
- **THEN** the dashboard shows one card, for `team-auth`

### Requirement: Card content
Each card SHALL show the repo (host, owner and name), the change name, its Project's code (or that it's personal),
the agents working on it with the member each one belongs to, the current section's number and title, the time of
the last report, and whether the change is active, idle or stale (see Quiet cards).

#### Scenario: Card shows identity and agents
- **WHEN** Emmz's `claude-code@vm-1` reports on `walking-skeleton` in `github.com/emmz/panopticon`
- **THEN** the card shows `emmz/panopticon` (host shown), `walking-skeleton`, the agent `claude-code` on
  `vm-1` with Emmz's display name, and when it last reported

#### Scenario: Agent without a member
- **WHEN** an agent reported with the Development bootstrap token, or before this change
- **THEN** its row shows the client and machine with no member name

#### Scenario: Card shows its Project
- **WHEN** a change is in Project `PAN`, and another is in the viewer's personal scope
- **THEN** the first card shows `PAN`, and the second shows that it's personal

### Requirement: Two progress bars
Each card SHALL show a progress bar for the current section and a progress bar for the overall change.
Each bar SHALL show both its fraction and its done/total counts, as defined in `task-progress`.

#### Scenario: Bars match tasks.md
- **WHEN** the current section has 2 of 5 tasks done and the change has 7 of 20 done
- **THEN** the section bar reads 2/5 (40%) and the overall bar reads 7/20 (35%)

#### Scenario: Empty change
- **WHEN** the change's `tasks.md` has no tasks
- **THEN** both bars show an empty state, not 0% or an error

### Requirement: Accessible progress
Progress bars SHALL be exposed to assistive technology with their label and current value, and SHALL
NOT rely on colour alone to convey state.

#### Scenario: Screen reader reads a bar
- **WHEN** a screen reader reaches a section bar
- **THEN** it announces the bar's label and value, e.g. "Section 2 progress, 40 percent"

### Requirement: Live updates
The dashboard SHALL update cards in place as reports arrive, without a page reload, and SHALL reconnect
by itself after the live connection drops.

#### Scenario: Report moves a bar
- **WHEN** a dashboard is open and a report ticks another task in the change
- **THEN** that card's bars update within a few seconds, with no reload

#### Scenario: New change appears
- **WHEN** a dashboard is open and the first report for a new change arrives
- **THEN** a new card appears at the top

#### Scenario: Connection drops and returns
- **WHEN** the live connection is lost and the server becomes reachable again
- **THEN** the dashboard reconnects, refreshes all cards to the current state, and shows that it was
  briefly offline while disconnected

### Requirement: Archived card leaves
When a change is archived, its card SHALL leave every open dashboard without a page reload.

#### Scenario: Archive while watching
- **WHEN** a dashboard is open and the archived report for `team-auth` is accepted
- **THEN** the `team-auth` card disappears within a few seconds, with no reload

### Requirement: Card opens its change
Each card SHALL link to its change's page (see `change-documents`), using an ordinary link that a keyboard and a
screen reader can follow, and that opens in a new tab when asked to.

#### Scenario: Open from a card
- **WHEN** a member selects the `team-auth` card's link
- **THEN** the change page for `team-auth` opens

### Requirement: History link
The signed-in header SHALL include a link to the history (see `change-history`).

#### Scenario: Go to history
- **WHEN** a signed-in member selects History in the header
- **THEN** the history page opens

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

### Requirement: Quiet thresholds are settings
The idle and stale thresholds SHALL be server settings, 30 minutes and 4 hours by default, applied the same way in
every open dashboard. The server SHALL refuse to start when the stale threshold isn't longer than the idle one, or
either isn't positive.

#### Scenario: Default thresholds
- **WHEN** neither threshold is set
- **THEN** cards go idle after 30 minutes and stale after 4 hours

#### Scenario: Thresholds out of order
- **WHEN** the idle threshold is set to 2 hours and the stale threshold to 1 hour
- **THEN** the server doesn't start, and its error names both settings

### Requirement: Tag edits are live
Open dashboards and change pages SHALL show tag changes without a reload. Tagging or untagging a change, and renaming
or deleting a Project tag, SHALL reach every member of that Project who can see the change. A change to a member's
personal tags SHALL reach only that member. A filtered dashboard SHALL add or remove cards as their tags change.

#### Scenario: An editor tags a change
- **WHEN** an editor adds `PAN/bug` to a change, and another `PAN` member has the dashboard open
- **THEN** that member's card shows `PAN/bug` without a reload

#### Scenario: A rename
- **WHEN** an editor renames `PAN/bug` to `defect`
- **THEN** every open `PAN` card and change page carrying it shows `PAN/defect`

#### Scenario: Personal tags stay private live
- **WHEN** Emmz's agent reports a new personal tag on a `PAN` change, and Sam has the dashboard open
- **THEN** Emmz's open card shows it, and Sam's card doesn't

#### Scenario: A filtered dashboard
- **WHEN** a dashboard is filtered by `PAN/bug`, and an editor removes `PAN/bug` from a shown change
- **THEN** that card leaves the dashboard, and the count updates

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
