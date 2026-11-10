# Spec Delta

## MODIFIED Requirements

### Requirement: Metrics page
A signed-in member SHALL be able to open a metrics page over archived changes, showing:
- the number of changes;
- their median time to archive;
- the median time per section;
- a weekly chart of the median time to archive, over the last 12 weeks;
- the slowest sections, each naming its change and linking to it.

Each chart's figures SHALL also be available as text. A personal change retired by linking (spec change-visibility,
Linking when the Project already has the change) SHALL NOT count: its work is measured on the Project's change.

#### Scenario: Team metrics
- **WHEN** three changes were archived in the last 12 weeks
- **THEN** the metrics page shows 3 changes, their median time to archive and per section, the weekly chart and the
  slowest sections

#### Scenario: Retired copies don't count
- **WHEN** `PAN`'s `cleanup` was archived, and Emmz's personal `cleanup` had been retired when she linked the repo
- **THEN** Emmz's metrics count one change, `PAN`'s

### Requirement: Metrics page filters
The metrics page SHALL take the same filters as the history (Project, repo, owner, person, machine, agent type), from
the page's address. A filter that matches no archived change SHALL say so, with a way to clear it.

#### Scenario: Nothing matches
- **WHEN** a member filters the metrics page by a machine no archived change ran on
- **THEN** the page says no archived changes match, with a way to clear the filters

#### Scenario: Filtered metrics
- **WHEN** a member filters the metrics page by repo `github.com/emmz/panopticon`
- **THEN** only that repo's archived changes are counted, and the address holds the filter

#### Scenario: Signed out
- **WHEN** someone without a session asks for the metrics or a change's metrics
- **THEN** the API answers 401

#### Scenario: One Project's metrics
- **WHEN** a member of `PAN` and `ACME` filters the metrics page by Project `PAN`
- **THEN** only `PAN`'s archived changes are counted
