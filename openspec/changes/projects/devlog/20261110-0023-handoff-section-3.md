# Handoff: §3 part-built. 3.1 is done, 3.2's code is written but untested, 3.3 isn't started

<!-- Section: 3 · Tasks: 3.1 done, 3.2 code only, 3.3 not started · Base: 9d0e4ec -->

Context reached 40% mid-section, so this is a cold handoff. **Nothing in §3 is committed.** The section commits once,
after 3.3 and the gates.

**Uncommitted files and their state:**
- **`src/Panopticon.Contracts/ReportProgress.cs`:** `ReportProgressArguments` gains an optional `Project` (string),
  `Names.Project = "project"`, and adds it in `ToToolArguments()` when set. Done.
- **`src/Panopticon.Server/Reporting/ReportRecorder.cs`:**
  - **New types:**
    - `ReportInput.ProjectId` (`Guid?`, init);
    - `RecordedReport.RetiredChangeId`;
    - the `ChangeScope(ProjectId, OwnerMemberId)` record;
    - `ReportRefusedException`, with a static `NotYourProject(string)` message naming the id, `list_projects` and
      `.panopticon.json`.
  - **`ResolveScopeAsync`** runs first in `UpsertAsync`, inside the transaction:
    - **A Project named:** the member must be in it, in any role. A bootstrap report, with no member, needs only that
      it exists. Otherwise it throws.
    - **No Project:** `(null, memberId)`, so a bootstrap report gets `(null, null)`.
  - **The archived-folder check** (D7.1) now also matches the scope.
  - **`FindOrAddChangeAsync`** now follows the D4 order. It locks the scope's own change (`LockActiveAsync`, using
    `IS NOT DISTINCT FROM` on both scope columns). For a member reporting into a Project, it also locks the
    reporter's own personal one. Then:
    - **Both exist:** the personal one gets `ArchivedAt = receivedAt` with no folder, and is returned as `Retired`.
    - **Only the personal one:** it's moved by setting `ProjectId` and clearing `OwnerMemberId`.
    - **Neither:** a new change with the scope.

    **This is 3.2's code. It compiles, and no 3.1 or older test broke, but there are no 3.2 tests yet.**
- **`src/Panopticon.Server/Reporting/ReportProgressTool.cs`:**
  - **The `project` parameter:** it's passed into `Validate`. A non-Guid gives an `McpException` with the
    `NotYourProject` message, and a Guid sets `ReportInput.ProjectId`.
  - **Refusals:** a `ReportRefusedException` becomes an `McpException`.
  - **A retired change:** `RetiredChangeId` triggers `broadcaster.PublishArchived(retired)`.
- **`tests/…/Infrastructure/ProjectTestHelpers.cs`** (new): `browser.CreateProjectAsync(code, ct)` and
  `browser.InviteToProjectAsync(projectId, email, role, ct)`, which drive the real API. Use these in later sections.
- **`tests/…/Reporting/ReportScopeTests.cs`** (new): six recorder tests for 3.1, all passing. Its `Harness` seeds
  members and Projects straight into the database, and records with `ReportRecorder` directly.
  - **For 3.2:** add its tests to this file, reusing `Harness.RecordAsync(member, project, change)` and `ScopeAsync`.
- **`tests/…/Mcp/ApiTokenReportTests.cs`:**
  - **`Two_members_on_one_machine_…`** now reports into a shared Project. It has to: unlinked work is each reporter's
    own now. It also no longer uses `POST /api/members`, which §5 removes, and Sam renames himself.
  - **New:** a theory for the not-a-member and malformed (`"PAN"`) ids at the MCP endpoint. 11 of 11 pass.
- **`tasks.md`:** 3.1 is ticked. The progress is 5 of 22.

**Last test runs:** `ReportScopeTests` 6/6, `ApiTokenReportTests` 11/11. Earlier, the whole server suite was 408/409
before the two-members test was rewritten, and that test now passes. **`make gates` hasn't been run since §2.**

**Gotchas from this section:**
- **`Change` isn't a record:** use named arguments in test helpers, not `with`.
- **Unique violations:** one aborts the Postgres transaction. Retries happen in `RecordAsync`, which clears the change
  tracker, never inside the transaction.

## Next

- **3.2:** write the tests in `ReportScopeTests` for the code that already exists:
  - **Linking mid-change:** Emmz reports `cleanup` twice with no Project, then once with `PAN`. There should be one
    change, now in `PAN`, with three report events, and the earliest event is the first report.
  - **A teammate's personal change is untouched** when Emmz links.
  - **An archived personal change isn't moved:** the linked report creates a new change in `PAN`.
  - **The Project already has the change** (Kit linked first): Emmz's linked report updates `PAN`'s change, and her
    personal change gets `ArchivedAt` set, `ArchiveFolder` null and `RetiredChangeId` equal to its id.
  - **The live events:** an MCP-level test in `ApiTokenReportTests` or `ReportProgressEndpointTests` that subscribes
    to `ChangeBroadcaster` (as at `ReportProgressEndpointTests.cs:213`) and sees an `Archived` event for the retired
    id.

  Fix the code if a test shows a bug, then tick 3.2.
- **3.3, the server's `list_projects` MCP tool** (design D6):
  - **The contracts:** `ListProjectsResult { projects: [{ id, name, code, role }] }` in `Panopticon.Contracts`, plus
    `[JsonSerializable]` entries in `ContractsJsonContext`.
  - **The tool:** an `[McpServerToolType]` next to `ReportProgressTool`.
  - **Behaviour:** a member gets their Projects with roles. A bootstrap reporter gets every Project, with role
    `viewer`.
  - **Tests:** MCP endpoint tests.
- **Then the §3 self-review, `make gates`, the section-done note, and one commit:** `feat(projects): reporting into
  Projects (§3)`, listing 3.1–3.3.
- **For §4.3:** cards will need the Project. Until then, the live `Updated` event after a move carries a card with no
  scope, and §4.2 adds the scope to the events.
