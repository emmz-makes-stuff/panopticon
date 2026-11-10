## MODIFIED Requirements

### Requirement: Metrics page filters
The metrics page SHALL take the same filters as the history (Project, repo, owner, person, machine, agent type,
tags), from the page's address. A filter that matches no archived change SHALL say so, with a way to clear it.

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

#### Scenario: Metrics by tag
- **WHEN** a member filters the metrics page by `PAN/bug`
- **THEN** only archived changes carrying `PAN/bug` are counted

#### Scenario: Metrics by a personal tag
- **WHEN** Emmz filters the team metrics by her personal tag `dev`
- **THEN** only archived changes where her own agents reported `dev` are counted
