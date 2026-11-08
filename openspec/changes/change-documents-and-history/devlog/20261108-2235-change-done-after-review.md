# Change done again, after the review fixes

<!-- Sections 1–10 · First base: 08ccacf^ · Head: fb96bba -->

All the tasks in §1–§10 are ticked.
- **§7–§10** are the `/code-review high` fixes Emmz chose: all four groups, plus "archived as `<folder>`" in the
  reply. Specs were amended in `f2667f4`.
- **Commits:** `ecfc223` (§7), `3c257cf` (§8), `86e2516` (§9), `fb96bba` (§10). Each section note has the details.

Cross-section review of `93f3d13..HEAD` (27 files, +564/−61):
- **Limits:** `DocumentLimits.MaxDocuments` sits beside the other shared limits, and both checks use it.
- **The reply's archive field:** `ReportProgressResult.Archived` is set only by the bridge. The server leaves it
  null, which isn't written, so its reply is unchanged.
- **One report at a time:** the per-change lock (§7.1) and the watcher's retry (§8.1) fit together. A retry goes
  through `ProgressReporter`, so it takes the same lock.
- **The router's `ViewHandle`:** only the change page uses it. Every other view returns a teardown or nothing, so
  it rebuilds as before.
- **No leftovers.**

Gates at `fb96bba`: `GATES_EXIT:0` (all labels 0). `make e2e`, last run at §8: `E2E_EXIT:0`.

## Next

- **Optional:** Emmz checks the 10.1 behaviour in a browser. Rebuild with `npm --prefix web run build`, restart the
  server, then try an in-page anchor in a rendered document and switching documents on a live change page.
- **Then** offer `/opsx:archive`, with the dashboard and this change's page open (§6 recipe step 3): the card
  should leave live, and the page should show the archived status.
