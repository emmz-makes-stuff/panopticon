## MODIFIED Requirements

### Requirement: Filters combine
Filters SHALL combine: a change is shown only when it matches every chosen filter. Choosing a new value for a
filter SHALL replace that filter's old value, except for the Tags filter, where each chosen tag is added (see Filter
by tags). A member SHALL be able to clear each filter, and all of them at once.

#### Scenario: Owner and person
- **WHEN** a member filters by owner `github.com/emmz` and by person Emmz
- **THEN** only `emmz`-owned changes that Emmz's agents reported on are shown

#### Scenario: Clear all
- **WHEN** a member with three filters chosen clears them all
- **THEN** every card is shown again

#### Scenario: Tags with another filter
- **WHEN** a member filters by repo `github.com/emmz/panopticon` and by tag `PAN/bug`
- **THEN** only that repo's changes carrying `PAN/bug` are shown

## ADDED Requirements

### Requirement: Filter by tags
The dashboard and the history SHALL each be narrowable by one or more tags, and a change SHALL match only when it has
every chosen tag. A tag SHALL be choosable by its key alone, matching any value. Personal tags SHALL match only the
viewer's own. Project tags SHALL be chosen by id, so renaming one SHALL NOT break a filter.

#### Scenario: Two tags
- **WHEN** a member filters by `PAN/bug` and `language: csharp`
- **THEN** only changes with both are shown

#### Scenario: Key alone
- **WHEN** a member filters by the key `language`, and changes carry `language: csharp` and `language: rust`
- **THEN** both are shown, and a change with no `language` tag isn't

#### Scenario: Removing one tag
- **WHEN** a member with `PAN/bug` and `dev` chosen removes `dev`
- **THEN** the filter keeps `PAN/bug`, and changes with `PAN/bug` are shown

#### Scenario: A deleted tag in the address
- **WHEN** a member opens an address filtered by a Project tag that's since been deleted
- **THEN** nothing matches, and the page offers to clear the filters

### Requirement: Tag filter choices
The Tags filter SHALL offer the tags on that page's changes (current cards for the dashboard, archived changes for the
history), as the viewer sees them: their own personal tags first, then each Project's tags with its code. Each key
that has values SHALL also be offered alone. Other members' personal tags SHALL never be offered.

#### Scenario: Grouped choices
- **WHEN** Emmz's cards carry her `dev`, her `language: csharp` and `PAN/bug`
- **THEN** the filter offers `dev`, `language`, `language: csharp`, then `PAN/bug`

#### Scenario: Nobody else's
- **WHEN** Sam's agents report `secret` on a `PAN` change Emmz can see
- **THEN** Emmz's Tags filter doesn't offer `secret`
