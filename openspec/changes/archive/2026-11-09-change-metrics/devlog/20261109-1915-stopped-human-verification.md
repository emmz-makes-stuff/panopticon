# Stopped: human verification (6.2)

<!-- Section: 6 · Tasks: 6.1 done, 6.2 waiting · Base: 7e103b5 -->

**6.1 is done** (ticked, uncommitted). `GATES_EXIT:0` on `7e103b5` (513 server tests and all web gates), then
`make e2e` gave `E2E_EXIT:0`: the image built, ran the new migration and started with the backfill. Nothing else is
uncommitted.

**What's blocked:** 6.2 needs Emmz's eyes: the look, live updates in a real browser, and the backfill on her real
local data. Nothing has been checked in a browser yet.

## Recipe

From the repo root, on `change/change-metrics`:

```sh
docker compose up -d --wait
npm --prefix web ci && npm --prefix web run build
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

Then sign in at <http://localhost:7480> as `dev@localhost.test` (the link is in the server log).

1. **The backfill.** On this first start, the server log should show `Progress backfill: filled N reports, M
   unreadable`, with N covering the reports from earlier changes. A warning naming an event id is expected only if
   a stored `tasks.md` is missing. Stop the server with Ctrl+C and start it again: this time there should be **no**
   backfill line.
2. **An archived change's panel.** Open History, then an archived change (e.g. `team-auth`). Under the header you
   should see:
   - "Took 5d 6h" (or whatever it took), and on hover the tooltip in words ("5 days 6 hours");
   - the line "Wall-clock time, first report to archive.";
   - one row per numbered section, each with its number, its title, a grey rule as long as its time against the
     longest, and its time.
   
   Do the times look believable against what you remember?
3. **An active change, live.** With `panopticon mcp` connected, open the page of an active change (this one,
   `change-metrics`, works). It should read "Running for …", with the current section's rule dashed and "in
   progress, 1h so far" at the right. Have the agent report after ticking the last task of a section (or tick one in
   `tasks.md` and report). The section should turn finished, with a solid rule and its time, **without a reload**.
4. **The metrics page.** The header should read Members, History, Metrics, Tokens. Choose **Metrics**, and check:
   - **The figures:** three of them (changes archived, the median time to archive, the median per section);
   - **The chart:** 12 weekly teal bars, with only the weeks that had archives showing a bar, labelled by their
     Monday;
   - **The slowest list:** the slowest sections, each change name linking to its page.
5. **Filters.**
   - Pick a repo: the numbers change in place, and the address gains `?repo=…`. Reload: the filter stays.
   - Pick a machine that never archived anything: you should get "No archived changes match these filters", and
     Clear filters should bring everything back.
6. **Looks.** Check both pages in light and dark (the system appearance), on desktop and at phone width (DevTools,
   about 375px):
   - **Section rows:** on a phone, each rule should drop under its title.
   - **The figures:** two to a row.
   - **The chart:** it scrolls sideways rather than shrinking its labels.
   - **The page overall:** no horizontal scroll.

## Next

- **Waiting on Emmz:** the results of the recipe above. If all is well, tick 6.2, write the section-done note, and
  commit §6 (`feat(change-metrics): integration and human verification (§6)`, which includes the 6.1 tick). Then
  the whole-change review (`git diff 6487960..HEAD`), and offer `/code-review`.
- **Anything that looks wrong** becomes a review-fix section (`## 7. Review fixes: …`) agreed with Emmz.
- **Still open:** do the metrics page's figures cover only the last 12 weeks, or all archived changes (as now)?
