## ADDED Requirements

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
