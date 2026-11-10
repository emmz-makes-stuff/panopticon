# Spec Delta

## MODIFIED Requirements

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
