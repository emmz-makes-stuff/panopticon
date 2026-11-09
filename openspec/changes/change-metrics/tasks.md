# Tasks

## 1. Server: per-report progress

- [x] 1.1 Add the migration for `report_events.overall_done`, `overall_total`, `current_section` and `sections` (jsonb), with `ReportRecorder` filling them from the parsed `tasks.md` (D1). Verify with a recorder test that a report stores its counts and its numbered sections only, and with a migration test that the new migration applies.
- [x] 1.2 Add the `ProgressBackfill` hosted service (D3). It fills events with null counts in batches from S3, stamps an unreadable one with `overall_total = -1` and logs it, and stops a batch on a transient error. Verify "Older reports filled in" and "A missing tasks.md" with server tests (seeded null rows, and one hash with no object), and that a second run touches nothing.

## 2. Server: metrics

- [x] 2.1 Add `SectionTimes.Compute(events)` and time to archive (D2). Verify every "Time per section" and "Time to archive" scenario with unit tests: a finished section, one in progress, one reopened, one never started, and unnumbered sections skipped.
- [x] 2.2 Add `GET /api/changes/{id}/metrics` (D4). Verify with endpoint tests for an archived and an active change, a 404 for an unknown id, and a 401 when signed out.
- [x] 2.3 Add `GET /api/metrics`, with `HistoryFilter` (D4): counts, medians, 12 weekly buckets and the 10 slowest sections. Verify "Team metrics" and "Filtered metrics" against seeded archived changes, that unreadable events are skipped, a 400 for a malformed filter, and a 401.

## 3. Web: change page metrics

- [x] 3.1 Add `formatDuration`, the metrics API calls and types (D4, D5). Verify `formatDuration` at its boundaries with unit tests.
- [x] 3.2 Add the change page's metrics panel (D5): "Took …" or "Running for …", the section rows, and in-progress marks. It refetches on the page's live refresh. Verify "Metrics on an archived change", "Live while open", and a change with no measured sections in `views/change` tests.

## 4. Web: metrics page

- [x] 4.1 Add `charts.ts`: an SVG bar chart with `role="img"`, a text summary and a hidden value table (D5). Verify with unit tests (bar heights relative to the largest, the summary text, and the table rows).
- [x] 4.2 Add the `/metrics` view, with the history's filter bar from the address, `update(params)` in place, the figures, the weekly chart, the slowest-sections links, and an empty filtered state with Clear, plus the Metrics header link (D5). Verify "Team metrics", "Filtered metrics", "Go to metrics", a failed load, and a filter change without a remount, in view and shell tests.

## 5. Design pass and docs

- [x] 5.1 Run the `frontend-design` skill for the change-page panel and the metrics page (figures, chart and list; desktop and phone; light and dark), within `web/DESIGN.md`. Add a "Metrics" section to it and apply the styles. Verify `make web-build` and `make web-lint`, and record the choices in a devlog note.
- [x] 5.2 Document the metrics in the README: what time per section and time to archive mean (wall-clock, matched by section number) and the backfill after deploying. Verify that the wording matches the spec.

## 6. Integration and human verification

- [x] 6.1 Run `make gates` and `make e2e` (the image runs the new migration and the backfill), and verify `GATES_EXIT:0` and `E2E_EXIT:0`.
- [x] 6.2 Write the human verification recipe in a devlog note, then stop for Emmz to run it:
  - the change page's panel on an active and an archived change, live as a section finishes;
  - the metrics page and its filters;
  - the backfill having filled older changes after a restart;
  - light and dark, desktop and phone.

## Workflow follow-up

- Run the whole-change review and offer `/code-review` before archiving.
- Archive with `/opsx:archive` once the review is done.
- Later changes from the roadmap: `panopticon setup`, Releases, and the Railway deploy (forwarded headers, a CSP header).
