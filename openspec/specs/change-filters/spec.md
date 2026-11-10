# change-filters Specification

## Purpose

Narrowing the live dashboard and the history to the changes a member cares about: by repo, owner/org, person,
machine, agent type and, on the dashboard, how recently each change was reported.

## Requirements

### Requirement: Filter by repo or owner
The dashboard and the history SHALL each be narrowable to one repo (host, owner and name) or to one owner/org (host
and owner). Hosts, owners and names SHALL be matched without regard to case, as change identity is. An owner that
contains '/' (e.g. a GitLab subgroup) SHALL be matched whole.

#### Scenario: One repo
- **WHEN** cards exist for `github.com/emmz/panopticon` and `github.com/emmz/other`, and a member filters by repo
  `github.com/emmz/panopticon`
- **THEN** only the `emmz/panopticon` cards are shown

#### Scenario: One owner across repos
- **WHEN** cards exist in `github.com/emmz/panopticon`, `github.com/emmz/other` and `github.com/acme/site`, and a
  member filters by owner `github.com/emmz`
- **THEN** the two `emmz` repos' cards are shown, and not `acme/site`

#### Scenario: Owner with a subgroup
- **WHEN** a member filters by owner `gitlab.com/team/web`
- **THEN** changes in `gitlab.com/team/web/app` are shown, and changes in `gitlab.com/team/app` are not

### Requirement: Filter by person
The dashboard and the history SHALL each be narrowable to one team member: a change matches when any of its agents
reported with that member's token. A member is chosen by identity, so renaming them SHALL NOT break a filter.

#### Scenario: Changes a member worked on
- **WHEN** Emmz's agent reported on `team-auth`, and only another member's agent on `walking-skeleton`
- **THEN** filtering by Emmz shows `team-auth` and not `walking-skeleton`

#### Scenario: Shared change
- **WHEN** both Emmz's and another member's agents reported on `team-auth`
- **THEN** `team-auth` is shown when filtering by either of them

### Requirement: Filter by machine or agent type
The dashboard and the history SHALL each be narrowable to one machine, or to one agent type (the MCP client name,
e.g. `claude-code`). A change matches when any of its agents ran on that machine, or is of that type.

#### Scenario: One machine
- **WHEN** `team-auth` has an agent on `vm-1` and `walking-skeleton` only on `laptop`
- **THEN** filtering by machine `vm-1` shows `team-auth` only

#### Scenario: One agent type
- **WHEN** `team-auth` was reported by `claude-code` and `walking-skeleton` only by `cursor`
- **THEN** filtering by agent type `claude-code` shows `team-auth` only

### Requirement: Filter by status
The dashboard SHALL be narrowable by card state: active, idle or stale (see `live-dashboard`, Quiet cards). The
history SHALL NOT offer a status filter, since every change there is archived.

#### Scenario: Only stale cards
- **WHEN** one card is active, one idle and one stale, and a member filters by status stale
- **THEN** only the stale card is shown

#### Scenario: A card goes stale while filtered
- **WHEN** a member is filtering by status stale, and an idle card passes the stale threshold with no new report
- **THEN** that card appears, with no reload

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

### Requirement: Filters in the address
The chosen filters SHALL be part of the page's address, so that a filtered page can be shared as a link, reloads
with the same filters, and moves between filter choices with the browser's Back and Forward. Changing filters
SHALL NOT reload the page.

#### Scenario: Share a filtered dashboard
- **WHEN** a member copies the address of the dashboard filtered by owner `github.com/emmz`, and a teammate opens it
- **THEN** the teammate sees the dashboard filtered by that owner

#### Scenario: Back undoes a filter
- **WHEN** a member adds a person filter, then presses Back
- **THEN** the dashboard shows the filters it had before, with no reload

#### Scenario: A filter value that matches nothing
- **WHEN** someone opens an address filtering by a repo that has no changes
- **THEN** the page shows that nothing matches the filters, with a way to clear them

### Requirement: Live updates respect filters
On a filtered dashboard, a live update SHALL show a card only if the change matches the filters, and SHALL remove a
card that no longer matches. The change count SHALL count the cards shown, and say when filters hide others.

#### Scenario: Report outside the filter
- **WHEN** the dashboard is filtered by repo `github.com/emmz/panopticon`, and the first report for a change in
  `github.com/emmz/other` arrives
- **THEN** no card appears for it, and the count says one more change is hidden by the filters

#### Scenario: A new agent joins a change
- **WHEN** the dashboard is filtered by machine `vm-2`, and an agent on `vm-2` reports on a change that until now
  only ran on `vm-1`
- **THEN** that change's card appears

### Requirement: History filtered by the server
The history's filters SHALL be applied by the server, so each page holds up to its usual number of matching
archived changes, newest archive first, and "Show more" continues within the same filters.

#### Scenario: Filtered paging
- **WHEN** 60 archived changes are in `github.com/emmz/panopticon` and 40 elsewhere, and a member filters the
  history by that repo
- **THEN** the first page shows 50 of the 60, newest archive first, and "Show more" shows the other 10

#### Scenario: Signed out
- **WHEN** someone without a session asks for the filtered history
- **THEN** the API answers 401

### Requirement: Filter choices
Each filter SHALL offer the values that exist on that page. The dashboard SHALL offer those on its current cards,
kept up to date as reports arrive. The history SHALL offer those among archived changes, from the server. A person
SHALL be offered by display name. An agent with no member SHALL NOT add a person choice.

#### Scenario: Dashboard choices follow the cards
- **WHEN** the dashboard shows changes in two repos, and a report arrives for a change in a third repo
- **THEN** the repo filter offers all three

#### Scenario: History choices
- **WHEN** archived changes exist in `emmz/panopticon` and `acme/site`, and active ones only in `emmz/other`
- **THEN** the history's repo filter offers `emmz/panopticon` and `acme/site`, and not `emmz/other`

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
