# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
