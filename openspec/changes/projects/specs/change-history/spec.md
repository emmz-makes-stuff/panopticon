# Spec Delta

## MODIFIED Requirements

### Requirement: History list
A signed-in member SHALL be able to list the archived changes they can see (spec change-visibility), the most
recently archived first. Each one SHALL show its repo (host, owner and name), change name, its Project's code (or
that it's personal), when it was archived, and the agents that worked on it with their members.

#### Scenario: Two archived changes
- **WHEN** `walking-skeleton` was archived on 2026-11-07 and `team-auth` on 2026-11-08
- **THEN** the history lists `team-auth` first, then `walking-skeleton`, each with its repo, archive date and agents

#### Scenario: Nothing archived yet
- **WHEN** no change has been archived
- **THEN** the history shows an empty state saying archived changes will appear there

#### Scenario: Signed out
- **WHEN** someone without a session opens the history
- **THEN** the API answers 401, and the app shows the sign-in page and then returns to the history

#### Scenario: History row shows its Project
- **WHEN** `team-auth` was archived in Project `PAN`
- **THEN** its history row shows `PAN`
