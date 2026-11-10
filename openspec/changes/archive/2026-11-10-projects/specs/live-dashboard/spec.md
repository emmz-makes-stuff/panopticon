# Spec Delta

## MODIFIED Requirements

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
