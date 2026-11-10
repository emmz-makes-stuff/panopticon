# Spec Delta

## Purpose

Who can see a change: each change belongs to one scope, a Project or the person who reported it, and every view of
changes shows a signed-in member only the changes in their scopes.

## ADDED Requirements

### Requirement: A change's scope
Each change SHALL belong to exactly one scope:
- **A Project:** when its reports named that Project;
- **Personal:** when they named none. It's then the reporting member's own.

A change's scope SHALL NOT change after it's created, except when its repo is linked to a Project (see Linking moves
personal work).

#### Scenario: A linked change
- **WHEN** Emmz reports `walking-skeleton` from a repo whose `.panopticon.json` names `PAN`
- **THEN** the change is in `PAN`'s scope

#### Scenario: An unlinked change
- **WHEN** Emmz reports `scratch` from a repo with no `.panopticon.json`
- **THEN** the change is in Emmz's personal scope

### Requirement: Linking moves personal work
When a member's report names a Project, and that member has an active personal change with the same repo and change
name, that change SHALL move into the Project, with its reports, agents and documents. Its time to archive and
section times are then kept whole. Only the reporting member's own personal change SHALL move. Archived changes
SHALL NOT move.

#### Scenario: Linking mid-change
- **WHEN** Emmz has reported `cleanup` in `emmz/panopticon` with no Project, then commits `.panopticon.json` naming
  `PAN`, and reports `cleanup` again
- **THEN** there's one `cleanup` change, in `PAN`, with all of its reports, and its time counts from the first report

#### Scenario: Someone else's personal change stays
- **WHEN** Kit also has a personal `cleanup` in the same repo, and Emmz's linked report moves hers into `PAN`
- **THEN** Kit's personal change is unchanged

### Requirement: Linking when the Project already has the change
When a member's report names a Project that already has an active change with the same repo and change name (a
teammate linked first), and the member also has an active personal one, the report SHALL update the Project's
change. The member's personal change SHALL be archived, so it leaves the dashboard and stays in their own history.

#### Scenario: A teammate linked first
- **WHEN** `PAN` already has `cleanup` from Kit's linked report, and Emmz, with a personal `cleanup` in the same repo,
  reports with `PAN`
- **THEN** Emmz's report updates `PAN`'s `cleanup`, and her personal `cleanup` leaves her dashboard for her history

### Requirement: Who sees a change
A signed-in member SHALL see a change only when it's in a Project they belong to, in any role, or in their own
personal scope. This SHALL hold everywhere changes appear:
- the dashboard and the live stream;
- change pages and their documents;
- the history and its filter choices;
- the metrics (a change's and the team's).

#### Scenario: Different Projects, different dashboards
- **WHEN** Kit belongs to `PAN` only, and there are active changes in `PAN` and in `ACME`
- **THEN** Kit's dashboard shows the `PAN` changes and not the `ACME` ones

#### Scenario: Personal work stays personal
- **WHEN** Emmz has an unlinked change, and Kit shares Project `PAN` with her
- **THEN** Emmz sees the change, and Kit doesn't

#### Scenario: Metrics count only what you can see
- **WHEN** archived changes exist in `PAN` and `ACME`, and Kit belongs to `PAN` only
- **THEN** Kit's metrics page counts only the `PAN` changes

### Requirement: Hidden changes don't exist
A change outside the viewer's scopes SHALL answer as though it didn't exist: its page, documents and metrics SHALL be
not found, and filter choices SHALL NOT include values that appear only on it.

#### Scenario: A link to another Project's change
- **WHEN** Kit opens the address of an `ACME` change
- **THEN** the page says the change wasn't found

#### Scenario: Choices don't leak
- **WHEN** only an `ACME` change was reported from machine `acme-vm`, and Kit doesn't belong to `ACME`
- **THEN** Kit's machine filter doesn't offer `acme-vm`

### Requirement: Live updates are scoped
The live stream SHALL send a viewer updates and archives only for changes in their scopes. A viewer removed from a
Project SHALL stop receiving its updates. Membership changes are covered by "Membership changes take effect live".

#### Scenario: Another Project's report
- **WHEN** Kit has the dashboard open and a report arrives for an `ACME` change
- **THEN** Kit's dashboard gets nothing

### Requirement: Membership changes take effect live
When someone is removed from a Project, or leaves it, their open pages SHALL drop it at once, with no reload:
- **The dashboard:** its cards and its filter choice go.
- **A change page of that Project:** it shows that the change wasn't found.

When someone joins a Project, their open dashboard SHALL show its active changes at once, and its live updates from
then on. History and metrics SHALL follow on their next load.

#### Scenario: Removed while watching
- **WHEN** Sam has the dashboard open, showing `PAN` cards, and an owner removes Sam from `PAN`
- **THEN** the `PAN` cards leave Sam's dashboard at once, and no later `PAN` update reaches it

#### Scenario: Removed while on a change page
- **WHEN** Sam has a `PAN` change's page open and is removed from `PAN`
- **THEN** the page says the change wasn't found

#### Scenario: Invited while watching
- **WHEN** Sam has the dashboard open and an owner invites Sam to `PAN`
- **THEN** `PAN`'s active cards appear on Sam's dashboard at once, and its later updates arrive live

### Requirement: Development bootstrap reports
A report made with the Development bootstrap token has no member. Such a report SHALL be able to name any existing
Project. With no Project, its change SHALL be visible to every signed-in member, as before Projects. Outside
Development the bootstrap token is refused, so this never applies.

#### Scenario: Bootstrap report with no Project
- **WHEN** in Development, a report with the bootstrap token names no Project
- **THEN** every signed-in member sees its change
