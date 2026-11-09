# change-metrics Specification

## Purpose

How long changes and their sections take: time to archive and time per section, measured from agents' reports, shown
on each change's page and across the team on a metrics page.

## Requirements

### Requirement: Time to archive
A change's time to archive SHALL be the time from its first accepted report to its archive. While a change is active,
its time so far SHALL be the time from its first report until now. Times SHALL be wall-clock time.

#### Scenario: Archived change
- **WHEN** `team-auth` was first reported on 3 Nov at 09:00 and archived on 8 Nov at 15:00
- **THEN** its time to archive is 5 days 6 hours

#### Scenario: Active change
- **WHEN** `filters-and-idle-cards` was first reported 2 days ago and isn't archived
- **THEN** its time so far is 2 days

### Requirement: Time per section
Each numbered `tasks.md` section SHALL have a start and, once finished, an end.
- **Start:** the first report in which the section has a ticked task or is the current section.
- **Finish:** the first report from which every one of its tasks stays ticked, through the change's last report.

A section's time SHALL be from its start to its finish. A started section that isn't finished SHALL be in progress,
with its time so far.

#### Scenario: A finished section
- **WHEN** section 2 is first the current section in a report at 10:00, and every task in it is ticked from a report
  at 13:30 onward
- **THEN** section 2 took 3 hours 30 minutes

#### Scenario: A section in progress
- **WHEN** section 3 was started an hour ago and two of its four tasks are ticked
- **THEN** section 3 is in progress, at 1 hour so far

#### Scenario: A section reopened
- **WHEN** section 1 was all ticked at 11:00, a task in it was unticked at 12:00, and it was all ticked again at 14:00
- **THEN** section 1 finished at 14:00

### Requirement: Sections unfinished at archive
On an archived change, a started section that never finished SHALL be marked unfinished, with its time up to the
archive.

#### Scenario: A section unfinished at archive
- **WHEN** a change was archived 2 hours after section 4 started, with section 4 not all ticked
- **THEN** section 4 is unfinished, at 2 hours

### Requirement: Sections not measured
A section with no tasks, or one never started, SHALL have no time. Sections without a number SHALL NOT be measured.

#### Scenario: Never started
- **WHEN** section 5 has no ticked task and has never been the current section
- **THEN** section 5 has no time

#### Scenario: Unnumbered section
- **WHEN** `tasks.md` has a "Workflow follow-up" section with no number
- **THEN** it isn't measured

### Requirement: Per-report progress is kept
Each accepted report SHALL keep the change's progress as that report saw it: the overall done and total, the current
section, and each numbered section's done and total. Reports accepted before this was kept SHALL have it filled in
from their stored `tasks.md`, without stopping the server from serving. A report whose `tasks.md` can't be read SHALL
be left out of the metrics, and not fail the rest.

#### Scenario: Older reports filled in
- **WHEN** the server starts with reports accepted before progress was kept
- **THEN** their progress is filled in from their stored `tasks.md`, and their changes' metrics include them

#### Scenario: A missing tasks.md
- **WHEN** an older report's stored `tasks.md` can't be found
- **THEN** that report is left out of the metrics, the server logs which one, and the other reports are filled in

#### Scenario: A tasks.md that fails to read
- **WHEN** reading an older report's stored `tasks.md` fails with a storage error
- **THEN** the server logs which report, fills in the reports after it, and tries that report again on its next start

### Requirement: Change page metrics
A change's page SHALL show its time to archive, or its time so far while active, and each measured section's time,
in section order, marking a section in progress. It SHALL update with the page's live updates. A change with no
measured section SHALL say so.

#### Scenario: Metrics on an archived change
- **WHEN** a member opens the archived `team-auth`
- **THEN** its page shows "Took 5d 6h", labelled "5 days 6 hours" for screen readers and as a tooltip, and each
  section's time

#### Scenario: Live while open
- **WHEN** a member has an active change's page open and a report finishes section 3
- **THEN** section 3's time shows as finished, without a reload

### Requirement: Metrics page
A signed-in member SHALL be able to open a metrics page over archived changes, showing:
- the number of changes;
- their median time to archive;
- the median time per section;
- a weekly chart of the median time to archive, over the last 12 weeks;
- the slowest sections, each naming its change and linking to it.

Each chart's figures SHALL also be available as text.

#### Scenario: Team metrics
- **WHEN** three changes were archived in the last 12 weeks
- **THEN** the metrics page shows 3 changes, their median time to archive and per section, the weekly chart and the
  slowest sections

### Requirement: Metrics page filters
The metrics page SHALL take the same filters as the history (repo, owner, person, machine, agent type), from the
page's address. A filter that matches no archived change SHALL say so, with a way to clear it.

#### Scenario: Nothing matches
- **WHEN** a member filters the metrics page by a machine no archived change ran on
- **THEN** the page says no archived changes match, with a way to clear the filters

#### Scenario: Filtered metrics
- **WHEN** a member filters the metrics page by repo `github.com/emmz/panopticon`
- **THEN** only that repo's archived changes are counted, and the address holds the filter

#### Scenario: Signed out
- **WHEN** someone without a session asks for the metrics or a change's metrics
- **THEN** the API answers 401

### Requirement: Metrics link
The signed-in header SHALL include a link to the metrics page.

#### Scenario: Go to metrics
- **WHEN** a signed-in member selects Metrics in the header
- **THEN** the metrics page opens
