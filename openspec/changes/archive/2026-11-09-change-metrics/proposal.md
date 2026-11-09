# Proposal

## Why

The dashboard shows where each change stands now, but not how long things take. Metrics are in v1 scope, and the
discovery left "which metrics?" open. Emmz chose two: **time per section** and **time to archive**. They answer
"how long does a change, and a section of one, really take us?", and they make a slow section stand out after the
fact, as quiet cards do while it's happening. The history and filters have just landed, so the archived changes these
numbers come from can now be narrowed the same way.

## What Changes

- **Time to archive:** for each change, the time from its first report to its archive. While a change is active,
  its page says how long it has been running.
- **Time per section:** for each numbered `tasks.md` section, the time from when work on it started (its first
  ticked task, or the first report naming it as the current section) to when all its tasks were ticked.
- **On the change page:** a small metrics panel shows the change's time so far (or its time to archive) and each
  section's time, with a section still in progress marked as such.
- **A metrics page (`/metrics`):** team-wide numbers over archived changes, narrowable with the history's filters
  (repo, owner, person, machine, agent type).
  - **Figures:** the number of changes archived, and the median time to archive and per section.
  - **Charts:** a weekly chart of median time to archive, and the slowest sections, each linking to its change.
  - **Drawing:** the charts are hand-built inline SVG in the lit-cells style, with no new dependency.
  - **Getting there:** the signed-in header links to the page.
- **Per-report progress is recorded.**
  - **What's stored:** each report keeps its overall counts and each numbered section's done and total. Today a
    report stores only the hash of its `tasks.md`.
  - **Older reports:** reports from before this change are filled in once, in the background, from their stored
    `tasks.md`.
  - **The data model:** this is a migration.

## Capabilities

### New Capabilities

- `change-metrics`: how long changes and their sections take. That covers:
  - how time to archive and time per section are defined;
  - the change page's metrics panel;
  - the metrics page and its filters, figures and charts;
  - recording per-report progress, including the backfill of older reports.

### Modified Capabilities

None. The change page and the header gain new parts, specified under `change-metrics`. The existing requirements of
`change-documents`, `live-dashboard` and `progress-reporting` don't change.

## Impact

- **Server:**
  - a migration adding progress counts to `report_events` (overall done/total, current section, and the numbered
    sections' counts as JSON);
  - `ReportRecorder` fills them for each report;
  - a background backfill reads older reports' `tasks.md` from S3;
  - new `GET /api/changes/{id}/metrics` and `GET /api/metrics` (member-only, with the history's filter parameters).
- **Web:**
  - a metrics panel on the change page;
  - a `/metrics` view with the filter bar;
  - small SVG chart helpers;
  - a header link;
  - a `frontend-design` pass, and a `web/DESIGN.md` section.
- **Docs:** the README, on what the metrics mean and the backfill.
- **Unchanged:** the CLI, the MCP contract, and the dashboard cards.
