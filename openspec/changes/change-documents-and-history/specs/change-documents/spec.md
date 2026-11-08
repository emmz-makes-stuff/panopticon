# Spec Delta

## Purpose

The drill-down view of one change: the list of its synced documents (proposal, design, tasks, spec deltas, devlog
notes), and each one rendered safely for a signed-in member, for active and archived changes alike.

## ADDED Requirements

### Requirement: Document list
A signed-in member SHALL be able to open a change by its id and see the change's identity and the list of its
latest synced documents: proposal, design, tasks, then spec deltas by capability, then devlog notes newest first.
A document the change doesn't have is left out of the list.

#### Scenario: Open an active change
- **WHEN** a member opens the change page for `team-auth`, whose last report synced a proposal, a design, tasks,
  two spec deltas and three devlog notes
- **THEN** the page lists the proposal, design and tasks, then both specs by capability name, then the three
  notes with the newest first

#### Scenario: Change with only tasks
- **WHEN** a change was only ever reported by a CLI that sends no documents besides `tasks.md`
- **THEN** the page lists only the tasks

#### Scenario: Unknown change
- **WHEN** someone opens the change page for an id the server doesn't know
- **THEN** the page says the change wasn't found and links back to the dashboard

### Requirement: Rendered documents
Selecting a document SHALL show it rendered from Markdown, with headings, lists, task checkboxes (ticked or not),
tables, and code. A YAML document SHALL be shown as preformatted text.

#### Scenario: Read the design
- **WHEN** a member selects the design
- **THEN** it is shown rendered, with its headings and tables

#### Scenario: Tasks show their ticks
- **WHEN** a member selects the tasks
- **THEN** each task shows as a checkbox that is ticked or not, as in `tasks.md`, and can't be changed

### Requirement: Safe rendering
Rendering SHALL NOT pass through raw HTML from a document, and SHALL NOT produce links or images whose scheme
isn't `http`, `https`, `mailto`, or a same-page anchor. Such content SHALL be shown as text or dropped.

#### Scenario: Script in a document
- **WHEN** a synced document contains `<script>alert(1)</script>`
- **THEN** the rendered page shows that text literally, and no script runs

#### Scenario: JavaScript link
- **WHEN** a synced document contains `[click](javascript:alert(1))`
- **THEN** the rendered page has no link with that target

### Requirement: Documents need a session
The document list and rendered documents SHALL be served only to a signed-in member. Without a session, the API
SHALL answer 401, and the app SHALL show the sign-in page and then return to the same change.

#### Scenario: Signed out
- **WHEN** someone without a session opens a change page
- **THEN** no document content is sent to them, and after signing in they land on that change page

### Requirement: Live drill-down
While a member has an active change's page open, a new report on that change SHALL refresh its document list
and the shown document without a page reload. When the change is archived while open, the page SHALL say so.

#### Scenario: Report while reading
- **WHEN** a member has `team-auth`'s tasks open and a report ticks another task
- **THEN** the shown tasks update within a few seconds, with no reload

#### Scenario: Archived while reading
- **WHEN** a member has an active change's page open and the change is archived
- **THEN** the page says it has been archived, and keeps showing its documents
