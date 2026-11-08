# Stopped: human verification of the change page, history and archive

<!-- Section: 6 · Tasks: 6.1 done, 6.2 written, awaiting Emmz · Base: dc89617 -->

§6 is the last section, and 6.2 ends with a stop for Emmz to run the recipe below. Its work is **uncommitted on
purpose**. The section commit lands once the recipe passes (or after fixes).

What's in the tree, uncommitted:
- **6.1, ticked:** `tests/Panopticon.Cli.IntegrationTests/ContainerEndToEndTests.cs`.
  - The fact is renamed `A_member_signs_in_logs_in_a_machine_reports_reads_the_documents_and_archives`.
  - It reports a whole folder (proposal, design, a spec, a devlog note, tasks), then checks
    `GET /api/changes/{id}`: no `ArchivedAt`, and exactly those five paths. It also renders `proposal.md`.
  - Then it moves the folder to `archive/2026-11-08-team-auth-e2e` and waits for `GET /api/history` to hold it.
    After that, the dashboard is empty and the change page has `ArchivedAt`.
  - **Gotcha:** the watcher only runs while `panopticon mcp` does. `ReportAsync` used to dispose the MCP client
    straight after the call, so it's now `BridgeAsync`, which returns a `BridgeSession` kept open through the archive.
    `WaitForAsync` is generic now.
  - `make e2e`: `E2E_EXIT:0`, 1 test, 1 succeeded, 0 skipped (1 m 26 s).
- **6.2, not ticked:** `README.md`.
  - The intro mentions change pages and the history.
  - There's a new "Change pages and the history" subsection under "Run it locally", including **"After deploying
    this version"**: report each already-archived change once.
  - The `make e2e` paragraph now describes the documents and archive steps.

Gates: `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` `FORMAT_EXIT:0` `WEB_INSTALL_EXIT:0` `WEB_BUILD_EXIT:0`
`WEB_TEST_EXIT:0` `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0` `VALIDATE_EXIT:0` `GATES_EXIT:0`

Also for Emmz: OrbStack's disk was at 93% (see the §2 note). `docker system df` shows 179.5 GB of local volumes,
99% reclaimable, and 17.8 GB of reclaimable images.

## The recipe

Setup (repo root; `make aot` already ran in the gates):

```sh
docker compose up -d --wait
npm --prefix web run build
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

Open <http://localhost:7480>, and sign in as `dev@localhost.test` (the link is in the server log). Then restart
`claude` in this repo, so `.mcp.json` starts the freshly built `artifacts/cli/panopticon`.

1. **Already-archived changes move to history.** In the new Claude session: "report progress for walking-skeleton,
   then for team-auth". Each returns its final progress (the reply doesn't say "archived": the spec only asks for
   the final progress). **Done 2026-11-08 20:11:** both archived, with 27 and 28 documents. **Check:** neither card is on the dashboard; History
   lists `team-auth` and `walking-skeleton`, each with the repo, the 2026-11-08 date and agents; each opens its
   change page with an archived status, and the documents are as in `openspec/changes/archive/2026-11-08-…`.
2. **The look**, in light and dark (DevTools → Rendering → `prefers-color-scheme`), at desktop width and at phone
   width (DevTools device toolbar, ~390 px):
   - the dashboard card's change name, a link to the change page;
   - the change page: the grouped list (a sticky column from 56rem, stacked below it), the current document's ink
     rule, the article measure, headings, code and tables;
   - the task lists: ticked = solid lit cells, unticked = visible outlined cells (open `tasks.md` of
     `change-documents-and-history` — report it once first);
   - the History page, its rows and the empty state (before step 1, if you like).
3. **A real `/opsx:archive` leaving an open dashboard.** Either:
   - **(a)** now, in a throwaway OpenSpec repo with an `origin` remote and the bridge connected: report a small
     change, keep its card open in the browser, run `/opsx:archive` on it, and within a few seconds see the card
     leave the dashboard and the change appear in History. On its open change page, see the archived status line;
   - **(b)** or defer it to archiving **this** change at the end (the workflow follow-up already calls that a live
     test of the watcher). Keep the dashboard and this change's page open while it archives.

**On PASS:** tick 6.2, write the section-done note (§6) and commit `feat(change-documents-and-history): container
e2e, README and verification (§6)`. Then run the whole-change review (`git diff 08ccacf^..HEAD`) and offer
`/code-review`.

**On FAIL:** record what looked wrong, add the fixes to §6 (or a new review-fix section if it's a design matter),
and agree them with Emmz before building.

## Next

- Waiting for Emmz to run steps 1–2 of the recipe above.
- **Emmz chose 3(b):** watch the live archive when this change itself is archived, with the dashboard and its change
  page open. Step 3 isn't part of this stop.
