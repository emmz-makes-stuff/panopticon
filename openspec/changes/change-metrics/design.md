# Design

## Context

See proposal.md for the why. Today:
- **Reports:** each accepted report is a `report_events` row (`ChangeId`, `AgentId`, `ReportedSection`,
  `TasksSha256`, `ReceivedAt`). The `tasks.md` content is stored in S3 under its hash.
- **Progress:** `TasksParser.Parse` gives `ParsedTasks` (sections with number, title, done and total, plus the
  overall counts). `CurrentSection.Select` picks the current section. Only the change's *latest* snapshot is kept, in
  `changes.snapshot` (JSON).
- **Changes:** `Change.ArchivedAt` marks the archive. The first report's time is `min(report_events.received_at)`.
- **The history:** filters go through `HistoryFilter` (server) and `filters.ts` with `createFilterBar` (web). The
  change page (`views/change.ts`) refreshes on `change-updated` events.

## Goals / Non-Goals

**Goals:**
- Section and archive times computed from stored per-report progress with plain queries, with no S3 reads at request
  time.
- One definition of a section's start and finish, used by the change page and the metrics page alike.
- Older reports backfilled once, safely, while the server runs.

**Non-Goals:**
- **Velocity or throughput** (tasks per day, changes per person). Emmz chose time per section and time to archive
  for v1.
- **Working-hours or idle-adjusted time.** Times are wall-clock. A change paused over a weekend counts the weekend.
- **Metrics on dashboard cards.** They stay as they are.
- **Per-agent or per-person section times.** A section's time belongs to the change.
- **Matching sections across renumbering.** A section is matched by its number, and its title is the latest seen.

## Decisions

### D1. Per-report progress columns on `report_events`

A migration adds:
- `overall_done` and `overall_total` (`int?`);
- `current_section` (`int?`);
- `sections` (`jsonb?`): `[{ "number", "title", "done", "total" }]` for numbered sections only.

Null means "not filled in yet" (backfill pending) or "couldn't be read" (see D3).
- **Recording:** `ReportRecorder` fills them from the `ParsedTasks` it already computes, and from
  `CurrentSection.Select`, in the same transaction.
- **Alternative considered:** a separate `report_section_progress` table, one row per section per report. It
  normalises better, but it multiplies the rows by the number of sections, while the JSON is read whole per change
  anyway. Rejected for v1.

### D2. Section times are computed in code from a change's ordered events

- **The function:** `SectionTimes.Compute(events)` (pure) walks a change's events by `ReceivedAt` and returns, per
  section number:
  - `startedAt`: the first event where `done > 0` or `current_section == number`;
  - `finishedAt`: the first event of the final run in which `done == total > 0`, with null if the last event isn't
    complete;
  - the latest title.
- **Time to archive:** `ArchivedAt − first ReceivedAt`. While active, it's `now − first`.
- **Why code, not SQL:** the "stays ticked through the last report" rule is a run-detection over the ordered events,
  which is clearer and testable in C#. A change has tens to hundreds of reports, so loading its events is cheap.

### D3. A background backfill

- **The service:** a hosted `ProgressBackfill` runs once on start, in batches (say 200 events). For each event with
  `overall_total IS NULL`, it fetches `tasks.md` by `TasksSha256`, parses it, and fills the columns.
- **Failures:**
  - A missing object or a parse failure stamps `overall_total = -1`, meaning "unreadable". Metrics skip those rows,
    and the server logs the event id.
  - A transient S3 error stops the batch with a warning, and the next start retries it.
- **Safety:** it's idempotent and only touches rows with null counts. It's off the request path, so it never stops
  serving.
- **The metrics while it runs:** they cover what has been filled so far.
- **Alternative considered:** doing it inside `--migrate`. Rejected, because EF migrations can't reach S3, and a
  Development start runs migrations in-process anyway.

### D4. API

All member-only.
- **`GET /api/changes/{id}/metrics`:** `{ firstReportedAt, archivedAt, durationSeconds, active, sections: [{ number,
  title, startedAt, finishedAt, durationSeconds, inProgress }] }`. Unknown id → 404.
- **`GET /api/metrics?repo&owner&member&machine&client`:**
  - **Scope:** archived changes, filtered by `HistoryFilter` (reused as-is, so a malformed value gets a 400 naming
    it).
  - **Shape:** `{ changes, medianArchiveSeconds, medianSectionSeconds, weeks: [{ weekStart, changes,
    medianArchiveSeconds }] × 12, slowestSections: [{ changeId, change, repo, number, title, durationSeconds }] × 10 }`.
  - **Weeks:** Monday-start weeks in UTC, by archive date.
  - **Medians:** computed in code over the filtered changes' events. That's one query for the changes and one for
    their events.

### D5. Web

- **The change page:** it gains a metrics panel under its header.
  - **Content:** "Took 5d 6h" (archived) or "Running for 2d" (active), then one row per measured section: number,
    title, duration, or "in progress · 1h so far".
  - **Loading:** it fetches `/api/changes/{id}/metrics` with the page, and again on the page's existing live refresh.
- **`/metrics` (`views/metrics.ts`):** the history's filter bar (`HISTORY_KEYS`, choices from
  `/api/history/filters`), then:
  - the figures;
  - an SVG bar chart of the weekly median time to archive;
  - the slowest-sections list.

  A filter change updates in place through `ViewHandle.update`, as the history does.
- **Charts:** `charts.ts` builds small inline SVG (bars in `--lit`, with an axis and labels in `--ink-muted`). Each
  chart has `role="img"` with a text summary, and a visually hidden table of its values.
- **Durations:** `formatDuration(seconds)` gives "45m", "3h 30m", "5d 6h".
- **The header:** a Metrics link, after History.
- **Look:** a `frontend-design` pass decides the panel's and the page's look, within `web/DESIGN.md`.

## Risks / Trade-offs

- **[Backfill load]** Many older reports means many S3 GETs at the first start. → It runs in batches, in the
  background, and only once per row.
- **[Section renumbering]** A change that renumbers its sections mid-way joins different sections under one
  number. → It's accepted and noted in the README. It's rare in practice.
- **[Wall-clock times]** A change parked for a week reads as slow. → It's stated on the page ("wall-clock time,
  first report to archive").
- **[Metrics query cost]** The page loads every filtered archived change's events. → History is small in v1; it can
  be cached or precomputed per change at archive if it grows.

## Migration Plan

- **Deploy:** one EF migration adding nullable columns. The backfill fills older rows after the deploy. Run
  `--migrate` as usual.
- **Rollback:** redeploy the previous image. The extra columns are ignored.
