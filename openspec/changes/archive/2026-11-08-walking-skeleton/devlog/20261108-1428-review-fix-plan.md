# Review-fix plan agreed: §9 and §10

<!-- After the change-done note · Base for §9: 4f0b116 -->

**Sources:**

- The whole-change coverage read (4 test gaps; see `20261108-1421-change-done.md`).
- A side agent's warning: the test container and the compose ports were published on **all interfaces**,
  with no sign-in and public credentials.
  - I stopped the `panopticon-check` container at once.
  - Compose is still up. Emmz was told `docker compose down` takes it off the network.
- `/code-review change/walking-skeleton` at medium effort, with no verify pass: 10 findings. I checked #1,
  #2 and #5 myself:
  - **#1 is real.** `ReportRecorder.UpsertAsync` sets `Snapshot`/`LastReportedAt` unconditionally.
  - **#2 is real.** `HttpClientTransport` has `DisposeAsync`, and it's never called.
  - **#5 conforms to the spec but is a product question.**

**Emmz agreed (2026-11-08):**

- **§9 (coverage and exposure):** the 4 tests, plus binding the compose ports and the README container
  command to `127.0.0.1`.
- **§10 (code review):** #1 only-if-newer, #2 dispose, #3 readable errors for IO/JSON, #4 SSE keep-alive,
  #6 HTML entities, #7 kill git on cancel, #8 anchored fallback regex, #10 resolve config once, and **#5:
  "Prefer explicit numbers"**. Change the task-progress spec wording too.
- **Not fixing: #9** (the double `/api/changes` fetch on load). It's harmless, and it keeps
  refresh-on-open simple.

**Notes for building:**

- 10.4 keep-alive: `SseFormatter` can't write comment lines from `SseItem`s, so send a named event (e.g.
  `ping`, with empty data) every ~25 s, merged with the change stream. The dashboard only listens for
  `change-updated`, so it ignores it. Make the interval injectable for the test.
- 10.1: compare `receivedAt` with the stored `LastReportedAt` inside the upsert. Consider an `xmin`
  concurrency token if the guard alone races. A test can simulate the order by recording the newer report
  first, then the older one, with `ManualClock`.
- 10.9: in `CurrentSection.Select`, prefer a section whose heading had an explicit number.
  `ParsedSection` may need an `IsNumbered` flag. Update `specs/task-progress/spec.md` (the Sections
  requirement) with a scenario.
- Any `|` in replacement text: use Edit, not perl.

## Next

- **§9, task 9.1** (base `4f0b116`): in `tests/Panopticon.Server.Tests/Persistence/ReportRecorderTests.cs`
  around line 94, assert `ReportEvent.AgentId` matches the reporting agent.
