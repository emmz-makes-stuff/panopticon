# live-dashboard Specification

## Purpose

The web dashboard's live view: one card per OpenSpec change that agents are reporting on, showing where
it stands at a glance and updating without a page refresh.

## Requirements

### Requirement: One card per change
The dashboard SHALL show one card for each change the server knows about. The most recently reported
change comes first.

#### Scenario: Two changes in two repos
- **WHEN** reports exist for `walking-skeleton` in `emmz/panopticon` and `add-auth` in `emmz/other`
- **THEN** the dashboard shows two cards, with the most recently reported one first

#### Scenario: No changes yet
- **WHEN** no report has ever been received
- **THEN** the dashboard shows an empty state explaining how to connect an agent

### Requirement: Card content
Each card SHALL show the repo (host, owner and name), the change name, the agents working on it with the
member each one belongs to, the current section's number and title, and the time of the last report.

#### Scenario: Card shows identity and agents
- **WHEN** Emmz's `claude-code@vm-1` reports on `walking-skeleton` in `github.com/emmz/panopticon`
- **THEN** the card shows `emmz/panopticon` (host shown), `walking-skeleton`, the agent `claude-code` on
  `vm-1` with Emmz's display name, and when it last reported

#### Scenario: Agent without a member
- **WHEN** an agent reported with the Development bootstrap token, or before this change
- **THEN** its row shows the client and machine with no member name

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
