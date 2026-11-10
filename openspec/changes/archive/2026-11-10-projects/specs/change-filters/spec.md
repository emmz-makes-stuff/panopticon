# Spec Delta

## ADDED Requirements

### Requirement: Filter by Project
The dashboard and the history SHALL each be narrowable to one of the viewer's Projects, or to their personal changes.
A Project SHALL be chosen by its id and offered by its code and name, so renaming it SHALL NOT break a filter. A
Project the viewer doesn't belong to SHALL match nothing.

#### Scenario: One Project
- **WHEN** a member of `PAN` and `ACME` filters the dashboard by `PAN`
- **THEN** only `PAN`'s cards are shown

#### Scenario: Personal only
- **WHEN** a member filters the history by "Personal"
- **THEN** only their own unlinked archived changes are listed

#### Scenario: Someone else's Project in the address
- **WHEN** Kit opens a dashboard address filtered by `ACME`'s id, and Kit doesn't belong to `ACME`
- **THEN** nothing matches, and the page offers to clear the filters

### Requirement: Project filter choices
The Project filter SHALL offer the viewer's Projects that have a change on that page (on its current cards for the
dashboard, among archived changes for the history), and "Personal" when the viewer has a personal change there.

#### Scenario: Only Projects with changes
- **WHEN** a member belongs to `PAN` and `ACME`, and only `PAN` has active changes
- **THEN** the dashboard's Project filter offers `PAN` and not `ACME`
