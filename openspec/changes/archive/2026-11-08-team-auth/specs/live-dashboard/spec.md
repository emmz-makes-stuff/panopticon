# Spec Delta

## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: Unauthenticated in this change
**Reason**: The dashboard now requires a signed-in team member (capability `dashboard-sign-in`).
**Migration**: Sign in with a magic link. Seed the first members with `Panopticon:InitialMembers`.
