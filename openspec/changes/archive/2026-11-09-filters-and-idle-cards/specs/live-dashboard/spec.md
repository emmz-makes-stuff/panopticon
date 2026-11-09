# Spec Delta

## MODIFIED Requirements

### Requirement: Card content
Each card SHALL show the repo (host, owner and name), the change name, the agents working on it with the
member each one belongs to, the current section's number and title, the time of the last report, and whether the
change is active, idle or stale (see Quiet cards).

#### Scenario: Card shows identity and agents
- **WHEN** Emmz's `claude-code@vm-1` reports on `walking-skeleton` in `github.com/emmz/panopticon`
- **THEN** the card shows `emmz/panopticon` (host shown), `walking-skeleton`, the agent `claude-code` on
  `vm-1` with Emmz's display name, and when it last reported

#### Scenario: Agent without a member
- **WHEN** an agent reported with the Development bootstrap token, or before this change
- **THEN** its row shows the client and machine with no member name

## ADDED Requirements

### Requirement: Quiet cards
A card SHALL be active until its change's last report is older than the idle threshold, idle until it is older than
the stale threshold, and stale after that. Each state SHALL look distinct and be named in text, not by colour alone.
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

### Requirement: Quiet state moves on by itself
A card's state SHALL follow the clock: an open dashboard SHALL move a card from active to idle to stale as the
thresholds pass, without a report or a reload. A new report on the change SHALL make its card active again at once.

#### Scenario: Goes idle while watching
- **WHEN** a dashboard is open and a change's last report passes 30 minutes old, with the default thresholds
- **THEN** its card is shown as idle within a minute, with no reload

#### Scenario: Report wakes a stale card
- **WHEN** a stale card's change is reported again
- **THEN** its card is active as soon as the update arrives

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
