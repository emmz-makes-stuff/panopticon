# Spec Delta

## MODIFIED Requirements

### Requirement: History list
A signed-in member SHALL be able to list the archived changes they can see (spec change-visibility), the most
recently archived first. Each one SHALL show its repo (host, owner and name), change name, its Project's code (or
that it's personal), when it was archived, and the agents that worked on it with their members. A personal change that
was retired because its repo was linked to a Project that already had the change (spec change-visibility, Linking
when the Project already has the change) SHALL stay in its owner's history, shown as linked to that Project in place
of an archive folder.

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

#### Scenario: A copy retired by linking
- **WHEN** Emmz's personal `cleanup` was retired because `PAN` already had `cleanup` when she linked the repo
- **THEN** her history lists it as "Linked to PAN", with no archive folder
