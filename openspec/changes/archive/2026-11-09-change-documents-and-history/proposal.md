# Proposal

## Why

Discovery promises that clicking a card opens all of a change's documents, and that finished changes stay
browsable as history. ADR-0001 already decided that the bridge would sync the whole change folder and notice
archives. Neither has been built yet:

- The bridge sends only `tasks.md`.
- The server has no notion of "archived".
- A card stays on the live view forever.

Two changes have now been archived (walking-skeleton and team-auth), and both are still invisible in
Panopticon.

## What Changes

- **The bridge syncs the whole change folder** on every report: `proposal.md`, `design.md`, `tasks.md`,
  `specs/**`, `devlog/*.md` and `.openspec.yaml`. Only Markdown and YAML are sent. The limits are 256 KB per file (`tasks.md` keeps its existing 1 MiB)
  and 4 MB per report, and the bridge refuses a report over them with an error that names the file or the total.
- **The bridge detects archives in two ways**, and both send the same "archived" report:
  - When a report names a change whose folder has moved to `openspec/changes/archive/<date>-<change>/`, the bridge
    reports the final documents from there, marked archived.
  - While it runs, the bridge watches `openspec/changes/` for the changes it has reported on in this session. When
    one of them moves to `archive/`, the bridge sends that report itself, so no agent action is needed.
- **The server stores every document content-addressed**, once per SHA-256, as it already does for `tasks.md`.
  It keeps each report's manifest (path → hash), so a later report timeline or "time travel" needs no migration.
- **Archiving is a server-side state.**
  - An archived change records when it was archived and its archive folder name.
  - It leaves the live view: a "removed" event reaches open dashboards.
  - A second archived report changes nothing.
  - A later change with the same name in the same repo starts a fresh card.
- **Drill-down:** `/changes/<id>` lists a change's documents and shows the selected one rendered. The list holds
  proposal, design, tasks, spec deltas by capability, and devlog notes newest first. This works for active and
  archived changes alike. An active change's page refreshes when that change reports.
- **History:** `/history` lists archived changes, the newest archive first, with repo, change, archive date and
  agents. Each one opens its drill-down, frozen at the final snapshot.
- **Rendering on the server** with Markdig, with raw HTML disabled and unsafe link schemes removed. Each
  document is rendered once per content hash and cached.
- **Compatible with older CLIs:** a report without documents (from a CLI built before this change) is still
  accepted. Only its `tasks.md` is kept, as today.

## Capabilities

### New Capabilities
- `change-documents`: the drill-down view of one change's documents, and how they are served and safely
  rendered.
- `change-history`: archived changes, how they're browsed, and what each one keeps.

### Modified Capabilities
- `cli-bridge`: "Change documents read locally" grows from `tasks.md` to the whole folder, with size limits.
  New requirements cover archive detection and the folder watcher.
- `progress-reporting`: "Report content" gains the documents and the archived marker, and "Document retention"
  keeps every document and each report's manifest. "Change identity" now holds among active changes only. A new
  requirement covers archived reports.
- `live-dashboard`: "One card per change" covers active changes only. A card leaves when its change is archived,
  and a card opens its drill-down.

## Impact

- **CLI:**
  - `ChangeDocuments` reads the folder, enforces the limits and finds `archive/*-<change>`.
  - `ProgressReporter` sends the documents and the archive marker.
  - A new watcher service lives in the bridge host.
  - The Native AOT publish must stay warning-free.
- **Contracts:** `ReportProgressArguments` gains optional `documents` (path and content pairs) and an optional
  `archive` (the folder name). Older servers ignore them, and older CLIs leave them out.
- **Server:**
  - One EF Core migration:
    - `changes` gains `archived_at` and `archive_folder`, and its unique index becomes partial on active changes.
    - New tables hold report manifests, and documents per change.
  - `ReportRecorder` stores the documents and handles archiving.
  - New endpoints serve the document list, rendered documents and history, all behind the session (`Member`
    policy).
  - The broadcaster sends a removal event.
- **Web:**
  - Two new views, `/changes/<id>` and `/history`, plus a History link in the header.
  - Cards become links.
  - `live.ts` handles the removal event.
  - Both new pages get a `frontend-design` pass, then Emmz's own check.
- **Dependencies:** none new. Markdig is already a server dependency: `TasksParser` parses `tasks.md` with it.
  Rendering uses its own pipeline next to the parser's.
- **Docs:** `docs/cli.md` gets what the bridge sends and how archive detection works. The README covers the
  drill-down and history pages.
