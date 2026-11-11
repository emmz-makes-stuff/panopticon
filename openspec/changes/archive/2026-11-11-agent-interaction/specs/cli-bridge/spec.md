# Spec Delta

## ADDED Requirements

### Requirement: Reported change recorded
After each report the server accepts, the bridge SHALL record the reported change's name for its working tree, in a
file on the machine outside the repo, keyed by the working tree's root, so `panopticon hook` can find it (see
`agent-presence`, A session's change). An archived report SHALL clear the record when it still names that change. A
failure to write the record SHALL NOT fail the report.

#### Scenario: Recorded
- **WHEN** the bridge reports `team-auth` from `~/src/panopticon` and the server accepts it
- **THEN** a hook run in `~/src/panopticon` finds `team-auth`

#### Scenario: Two worktrees
- **WHEN** one worktree of a repo reports `tags` and another reports `projects`
- **THEN** a hook in each worktree finds its own change

#### Scenario: Archived
- **WHEN** the bridge reports `team-auth` archived, and the record still names `team-auth`
- **THEN** a hook in that working tree finds no recorded change

#### Scenario: Refused report
- **WHEN** the server refuses a report of `tags`
- **THEN** the record keeps the change it named before
