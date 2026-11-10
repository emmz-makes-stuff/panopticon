# Decision: retired personal copies are left out of the metrics, and show as "Linked to <Project>" in the history

<!-- Section: between 4 and 5 · Decision by Emmz · Base: 437141a -->

**What raised it:** a side agent's note, then a check of the code. A personal change retired by linking (D4: the
Project already had the change) gets `ArchivedAt` and no `ArchiveFolder`. Two things go wrong:
- **The metrics:** `MetricsReader.ForTeamAsync` counts every change with `ArchivedAt != null`, so the copy counts as a
  finished change, with a time cut short by the link.
- **The history:** `ChangePages.HistoryAsync` writes `ArchiveFolder!`, so the row reaches the web with
  `archiveFolder: null`, while `web/src/types.ts:81` types it as `string`.

**Emmz's answers (2026-11-10):**
- **The team metrics leave retired copies out.** Only real archives (with a folder) count. The work is measured on the
  Project's change already.
- **The history keeps them,** as the spec says, labelled "Linked to <Project>" in place of the folder. So the server
  records which Project took the work over, and `archiveFolder` becomes nullable on history entries.

**Where the tasks go (Emmz agreed):**
- **5.0, on the server,** first in §5: the `retired_to_project_id` column and its migration, the metrics leaving retired
  copies out, and history entries with a nullable `archiveFolder` and a `linkedTo` Project.
- **8.3, on the web:** the "Linked to <code>" history row.

Design D4 and the change-history and change-metrics delta specs now say so (`VALIDATE_EXIT:0`).

## Next

- **5.0:** start here. Base `437141a` plus this docs commit.
- **Then 5.1.**
