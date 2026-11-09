# Design

## Context

See `proposal.md` for the motivation. The current state that shapes this design:

- **Bridge:** `ChangeDocuments.ReadTasksAsync` reads only `tasks.md`. `ProgressReporter` builds a
  `ReportProgressArguments(repo, change, agent, tasksMarkdown, section)` and forwards it to the server's `/mcp` as
  a tool call. The bridge is a long-lived stdio host (`BridgeHost`), started by the agent in the repo.
- **Server:**
  - `ReportRecorder` stores `tasks.md` content-addressed in S3 (`IDocumentStore.PutAsync` returns the SHA-256),
    upserts repo, change and agent under a `FOR UPDATE` lock on the change row, and appends a `report_events` row
    with `tasks_sha256`.
  - `changes` is unique on `(repo_id, name_key)`.
  - `ChangeBroadcaster` fans out `ChangeCard`s, and the SSE stream sends them as `change-updated`.
- **Markdown:** Markdig is already referenced. `TasksParser` uses its own pipeline, with HTML disabled, to count
  checkboxes.
- **Web:** a small router with views per path (`/`, `/members`, `/tokens`, `/device`, the sign-in pages). An
  `api.ts` wrapper handles 401s. `live.ts` runs one `EventSource` for the cards view.

## Goals / Non-Goals

**Goals:**
- Keep ADR-0001's promise: the whole change folder syncs on every report, and archives are noticed without the
  agent knowing about Panopticon.
- Store enough per report (manifests) that a timeline or time travel is UI work only.
- One safe rendering path, tested on the server.

**Non-Goals:**
- Filtering or search in the history (that's the filtering change), a per-report timeline, time travel, and
  diffs between versions.
- Rendering anything but Markdown and YAML. Mermaid diagrams aren't rendered, and images aren't synced.
- Watching changes the bridge never reported, or repos other than the bridge's own.
- Changing `tasks.md`'s 1 MiB limit (`task-progress`).

## Decisions

### Wire contract

- **D1. `ReportProgressArguments` gains two optional fields:**
  - `documents: [{path, content}]`;
  - `archive: string?`, the archive folder name, e.g. `2026-11-08-team-auth`.

  `tasksMarkdown` stays required and separate. That keeps the server's existing validation and parser
  untouched, and old CLIs keep working (the "Report from an older CLI" scenario).
  - The new bridge also lists `tasks.md` in `documents`, so the manifest is complete.
  - The server checks that `tasks.md` in `documents` matches `tasksMarkdown` when both are present. A mismatch is
    a bad request.
  - Older servers bind only the tool's named parameters, so an older server given a new CLI's report ignores the
    extra fields. That case isn't required, but it doesn't break.
  - Rejected: a separate `sync_documents` tool, which would need two calls per report and a window where progress
    and documents disagree. Also rejected: sending only changed documents after a hash handshake. That saves
    upload bytes but adds a round trip, and the limits keep a full upload small.

### Bridge

- **D2. Reading the folder.** `ChangeDocuments` enumerates the change folder recursively:
  - Files ending `.md`, `.yaml` or `.yml` are included, in ordinal path order, using `/` separators.
  - Symbolic links and reparse points are skipped (`FileAttributes.ReparsePoint`), as are directories that are
    links, so nothing outside the folder is read.
  - Each file is read as UTF-8. A file that isn't valid UTF-8 is a `BridgeException` naming it.
  - The limits are checked on byte length, before anything is sent: `tasks.md` 1 MiB, any other file 256 KB, the
    total 4 MB.
- **D3. Finding an archive.** When `openspec/changes/<change>/` is missing, the bridge looks in
  `openspec/changes/archive/` for directories matching `^\d{4}-\d{2}-\d{2}-<change>$`, compared ordinally, and
  takes the greatest name, which is the latest date. The same folder rules (D2) apply, and the report carries
  `archive = <folder name>`. If nothing matches, the error names the paths it looked for. `ResolveChangeDirectory`
  still guards the name first.
- **D4. The watcher.**
  - It's a hosted `ArchiveWatcher` in `BridgeHost`.
  - `ProgressReporter` records each change it reports successfully, along with the client name used, in a small
    in-memory registry.
  - The watcher runs a `FileSystemWatcher` on `openspec/changes/`, non-recursive, for directory renames and
    deletes. It debounces for 1 s, then for each registered change whose folder has gone, it runs the D3 lookup.
    If there's an archive, it reports it archived as the recorded client, then forgets that change.
  - Rejected: polling every N seconds. That's simpler, but either slow or wasteful. A rescan on watcher error (a
    buffer overflow) covers what a `FileSystemWatcher` can miss.
  - Failures are logged at Warning to stderr and never thrown.
  - The repo root comes from the first successful report, since that's when `GitRepository.FindRootAsync` runs. No
    report means nothing to watch.
  - It's AOT-safe: `FileSystemWatcher` needs no reflection.

### Server

- **D5. Data model.** There is one migration:
  - **`changes`** gains `archived_at timestamptz null` and `archive_folder text null`.
    - The unique index `(repo_id, name_key)` becomes partial: `WHERE archived_at IS NULL`.
    - A new unique index `(repo_id, archive_folder)` is `WHERE archive_folder IS NOT NULL`, which makes D7's
      idempotency a lookup.
  - **`report_documents`** (`report_event_id`, `path`, `sha256`, `size_bytes`) is the manifest, with primary key
    `(report_event_id, path)`. Old events have no rows, and their `tasks_sha256` stays as it is.
  - **`change_documents`** (`change_id`, `path`, `sha256`, `size_bytes`, `updated_at`) holds each change's latest
    documents, with primary key `(change_id, path)`. It's replaced whole on each newer report, under the existing
    change-row lock, and it's what the drill-down reads.
    - Rejected: deriving the latest set from the newest event's manifest each time. That's a correct but slower
      query on every page view, and the "only a newer report moves the change on" rule already exists for the
      snapshot.
  - Paths are stored as given, relative and with `/` separators, after D6's checks.
- **D6. Server-side checks** run in `ReportProgressTool.Validate`, before anything is stored:
  - **Paths:** no rooted paths, no `\`, no `.` or `..` segments, no empty segments, at most 512 characters, and
    the extension must be `.md`, `.yaml` or `.yml`.
  - **Uniqueness:** a duplicate path is rejected.
  - **Size:** the limits from D2, on UTF-8 byte counts.
  - The server repeats the bridge's checks because the server is the trust boundary.
- **D7. Archiving** happens in `ReportRecorder`, in the same transaction as the report:
  1. If `archive` is set and `(repo, archive_folder)` already exists, return that change's snapshot and change
     nothing, not even a new event. This is the "Archived twice" scenario.
  2. Otherwise, find the active change by `(repo, name_key)` under `FOR UPDATE`, or create one. Apply the report as
     usual. Then, if `archive` is set, set `archived_at = now` and `archive_folder`.
  3. If `archive` is null, it's an ordinary report. Because the unique index is partial, a reused name gets a fresh
     active row.

  After commit, the tool publishes `change-archived {changeId}` instead of `change-updated` for an archived report.
- **D8. Rendering.** A `DocumentRenderer` uses its own Markdig pipeline:
  - `UseAdvancedExtensions()` minus anything that emits raw HTML, plus `DisableHtml()`. Task lists render as
    disabled checkboxes, and tables, emphasis extras and auto-identifiers are on.
  - A post-render pass walks the AST before writing, through a `LinkInline`/`AutolinkInline` rewriter. It drops the
    URL of any link or image whose scheme isn't `http`, `https` or `mailto`, keeping the text, and allows only
    `#fragment` relative URLs.
  - Images are kept as links, not `<img>`, so a document can't make browsers fetch arbitrary URLs.
  - YAML is HTML-encoded inside `<pre><code>`.
  - Results are cached in a size-bounded `MemoryCache` by `sha256`. Content is immutable, so entries never go stale.
  - Rejected: client-side `markdown-it` (Emmz chose the server).
- **D9. Endpoints**, all with `RequireAuthorization(Member)`:
  - `GET /api/changes/{id}` gives the change header: repo, name, snapshot, agents with members, `archivedAt`,
    `archiveFolder`, and the document list as `[{path, kind, title, sizeBytes}]`.
    - The order is proposal, design, tasks, `specs/*` by capability path, `devlog/*` by file name descending, then
      anything else by path.
    - `kind` is one of `proposal`, `design`, `tasks`, `spec`, `devlog`, `meta` or `other`.
    - `title` is the capability for specs, and the slug and time from the file name for devlog notes.
  - `GET /api/changes/{id}/documents/{**path}` returns `{path, html}`. An unknown change or path is a 404.
  - `GET /api/history?before=<archivedAt>&limit=50` gives archived changes with archive time descending, as
    keyset pages. Each row has repo, name, `archivedAt`, `archiveFolder` and agents.
  - `GET /api/changes` (the cards) now filters `archived_at IS NULL`.
- **D10. Live events.** `ChangeBroadcaster` carries a small union: `Updated(ChangeCard)` or `Archived(Guid)`.
  `StreamAsync` writes `change-updated` or `change-archived` as before. Both use the existing 64-slot drop-oldest
  channel, and a reconnect still refreshes everything.

### Web

- **D11. Views.**
  - **`/changes/<id>` (`changeView`):** the header shows repo and change, plus an "Archived 8 Nov 2026" note when
    archived. The document list is a `<nav>` of route links (`?doc=<path>`, so a document is linkable and survives
    a reload), and the rendered document sits in an `<article class="doc">`. It's two columns at desktop width and
    stacked on a phone. The list defaults to the proposal, falling back to tasks.
  - **Rendered HTML:** inserted with `innerHTML` only from the D9 document endpoint. That's the one place the app
    takes HTML, and a comment points at D8. ESLint's `no-unsanitized`-style concern is documented there instead of
    suppressed silently.
  - **`/history` (`historyView`):** rows between hairlines, like Members, each linking to `/changes/<id>`. A
    "Show more" button fetches the next keyset page.
  - **Cards:** each gets an `<a data-route href="/changes/<id>">` around the change name, which is the card's
    accessible name. Making the whole card one link would wrap the progress bars' accessible content inside it.
  - **Header:** gains History between Members and Tokens.
  - **Live updates:** `live.ts` handles `change-archived` by removing that card. `changeView` opens its own
    `connectLive`-style subscription, scoped to its change id. It refetches the header and the shown document on
    `change-updated` for that id, and shows the archived note on `change-archived`.
  - Both new pages go through the `frontend-design` skill, within `web/DESIGN.md`'s rules, before Emmz's check.

### Documentation

- **D12.** `docs/cli.md` gets "What the bridge sends", with the folder rules and limits, and "Archives", covering
  the two ways and that the watcher only covers changes reported in this session. The README gets a short note on
  the change page and the history.

## Risks / Trade-offs

- **[A large devlog makes every report heavy]** → The 4 MB cap. A typical change is around 100 KB. A hash handshake
  can come later without a spec change, because the contract has room for it.
- **[`FileSystemWatcher` misses events]** on network filesystems, or under heavy churn → Rescan on `Error`, and the
  explicit "report after archive" path (choice A) still covers it.
- **[The archive happens while the bridge isn't running]** (an archive by hand in a shell) → The next report for
  that change name finds the archive (D3). If nothing ever reports it again, the card stays. That's documented;
  `panopticon sync` could cover it later.
- **[`innerHTML` of server-rendered HTML]** → One code path, with D8's tests (script, `javascript:`, `data:`,
  images, raw HTML blocks and inline HTML). The CSP header that would add defence in depth is noted as a follow-up
  for the deploy change.
- **[The partial unique index on `changes`]** → Postgres supports partial unique indexes, and the D2 index from
  team-auth already relies on Postgres 15 or later. EF Core 10 models them with `HasFilter`.
- **[A change archived before this change shipped]** (walking-skeleton, team-auth) → Their cards are live rows. The
  first archived report from a new CLI archives them. Until then they stay as cards. Emmz can trigger it by
  reporting each name once from its repo.

## Migration Plan

- Deploying is additive: the migration adds nullable columns, two tables, and swaps one index for a partial one.
  The old CLIs keep working.
- Rollback: the previous server ignores the new columns and tables. The partial index would let two active
  same-name rows exist only after an archive, which the old server never makes. Dropping the migration is safe.
- After deploying, report `walking-skeleton` and `team-auth` once from this repo with the new CLI, so they move to
  history. That's the human verification step.
