# Tasks

## 1. Wire contract and the data model

- [x] 1.1 Add the optional `documents` (`[{path, content}]`) and `archive` fields to `ReportProgressArguments`, `ToToolArguments` and `ContractsJsonContext` (D1). Verify with `ContractsTests` that a report without them serializes exactly as before, and that one with them round-trips.
- [x] 1.2 Add the D5 migration: `changes.archived_at` and `archive_folder`, the partial `(repo_id, name_key) WHERE archived_at IS NULL` index, the `(repo_id, archive_folder)` index, and the `report_documents` and `change_documents` tables. Verify a Testcontainers `MigrationTests` case applies it to an empty database and to one with team-auth data, and that two active same-name changes are still refused.

## 2. Server: recording documents and archives

- [x] 2.1 Validate documents in `ReportProgressTool` (D6: path rules, extensions, duplicates, 256 KB per file, 1 MiB for `tasks.md`, 4 MB in total, and `tasks.md` matching `tasksMarkdown`). Verify the "Path escapes the folder" and "Oversized report" scenarios, plus a test for each other rule, all recording nothing.
- [x] 2.2 Store each document content-addressed, and write the report's manifest (`report_documents`) and the change's latest set (`change_documents`, replaced only by a newer report) in `ReportRecorder`. Verify "Unchanged design stored once", "Manifest per report", "Report from an older CLI" (manifest holds only `tasks.md`), and that an older report committing late doesn't replace a newer document set.
- [x] 2.3 Implement archiving (D7): look up by `(repo, archive_folder)` first for idempotency, archive the active change after applying the report, create an archived change when none exists, and keep the partial-index behaviour for reused names. Verify "Change archived", "Archived twice", "Archived with no active change", "Name reused after archive" and "Later reports don't change it".
- [x] 2.4 Publish `change-archived {changeId}` through the broadcaster and SSE stream (D10), and filter archived changes out of `GET /api/changes`. Verify with `DashboardEndpointTests` that an open stream receives `change-archived` after an archived report, and that the card list leaves it out ("Archived change has no card").

## 3. Server: rendering and the read APIs

- [x] 3.1 Add `DocumentRenderer` (D8): its own Markdig pipeline with `DisableHtml()`, task lists as disabled checkboxes, tables, the link and image scheme filter, images as links, YAML as escaped `<pre>`, and a hash-keyed bounded cache. Verify unit tests for "Script in a document", "JavaScript link", `data:` and `vbscript:` URLs, inline and block raw HTML, images, task checkboxes, tables and YAML.
- [x] 3.2 Implement `GET /api/changes/{id}` (header, agents, archive fields, and the ordered document list with `kind` and `title`) and `GET /api/changes/{id}/documents/{**path}` (D9), both needing a session. Verify "Open an active change", "Change with only tasks", "Unknown change" (404), an unknown document path (404), the ordering of specs and devlog notes, and the 401 without a session.
- [x] 3.3 Implement `GET /api/history` (D9: archived changes, archive time descending, keyset pages of 50, needing a session). Verify "Two archived changes", "Nothing archived yet", the paging boundary, and the 401 without a session.

## 4. Bridge: the whole folder and archives

- [x] 4.1 Read the whole change folder in `ChangeDocuments` (D2: `.md`, `.yaml` and `.yml`, recursive, `/` paths in ordinal order, links skipped, UTF-8, the size limits) and send it from `ProgressReporter`. Verify the "Whole folder forwarded", "Other files skipped", "Link out of the folder" (Unix), "One file too large" and "Folder too large" scenarios as `Panopticon.Cli.Tests`, and that "Change exists", "Change missing" and "Path traversal" still pass.
- [x] 4.2 Find an archived folder when the change's folder is missing (D3: `archive/<YYYY-MM-DD>-<change>`, latest wins) and report it with `archive` set. Verify "Report after archive" and "Archived twice under one name" as tests, and an end-to-end `BridgeTests` case where the server then lists the change in history.
- [x] 4.3 Add the `ArchiveWatcher` hosted service (D4: a registry of reported changes with their client name, a `FileSystemWatcher` with a 1 s debounce and rescan on error, failures logged to stderr). Verify "Archive while the bridge runs", "Unreported change archived" and "Server down at archive time" as `BridgeTests` using a temp git repo, and that `make aot` publishes with no new warnings.
- [x] 4.4 Document in `docs/cli.md` what the bridge sends (folder rules and limits) and how archives are detected (both ways, and the watcher only covering changes reported in this session). Verify the doc against the tests' behaviour by rereading the scenarios.

## 5. Web: the change page, history and cards

- [ ] 5.1 Add API helpers and types for the change header, a document and history pages, and handle `change-archived` in `live.ts` by removing the card. Verify Vitest tests for "Archive while watching" with the fake `EventSource`, and the helpers' 401 handling.
- [ ] 5.2 Add the `/changes/<id>` view (D11): header with the archived note, the document list as route links with `?doc=`, the rendered document, a not-found state, and a live subscription for that change (refetch on `change-updated`, the archived note on `change-archived`). Verify Vitest tests for "Open an active change" (order and default document), selecting a document, "Unknown change", "Report while reading", "Archived while reading", and "Signed out" (returns to the same page after sign-in).
- [ ] 5.3 Add the `/history` view (rows linking to the change page, "Show more" paging, an empty state) and the History link in the header. Make each card's change name a route link to its page. Verify Vitest tests for "Two archived changes", "Nothing archived yet", paging, "Go to history" and "Open from a card", and that the card's link works by keyboard.
- [ ] 5.4 Style both pages and the card link with the `frontend-design` skill within `web/DESIGN.md` (two columns at desktop width, stacked at 375 px, rendered-Markdown typography, light and dark), and verify `make web-build`, `make web-lint` and `make web-format` pass.

## 6. Integration and human verification

- [ ] 6.1 Extend the container end-to-end test: report a change through the published `panopticon mcp` with a full folder, check that `GET /api/changes/{id}` lists its documents, then move the folder to `archive/` and check that the watcher's report lands it in `GET /api/history`. Verify `make e2e` passes.
- [ ] 6.2 Update the README (the change page and history; reporting once after deploy moves already-archived changes to history), and write the human verification recipe in a devlog note:
  - the look of both pages, in light and dark, at desktop and phone width;
  - a real `/opsx:archive` watched leaving an open dashboard;
  - `walking-skeleton` and `team-auth` moved to history by reporting each once.

  Then stop for Emmz to run it.

## Workflow follow-up

- Run the whole-change review and offer `/code-review` before archiving.
- Archive with `/opsx:archive` once the review is done. That archive is also a live test of the watcher.
- Later changes from the roadmap: filtering and idle/stale, metrics, then `panopticon setup`, Releases and the Railway deploy (forwarded headers, a CSP header).
