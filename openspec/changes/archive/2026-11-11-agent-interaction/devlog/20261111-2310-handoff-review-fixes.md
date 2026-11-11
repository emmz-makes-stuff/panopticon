# Handoff: the code review is done; §9 and §10 (review fixes) are planned and agreed, and not started

<!-- Section: 9 · Tasks: none started · Base: db196c0 plus this docs commit -->

**Emmz's answers (2026-11-11):**

- Both of the self-review's fixes go in: the answer-list wording, and `WaitAsync` using the `TimeProvider`.
- Run `/code-review` first. It ran at medium effort with no verify pass, and gave 8 candidates.
- She chose all four groups of findings below.

**I checked each candidate against the code** (file:line as the review gave them):

- **F1** `Reporting/ReportProgressTool.cs:73`:
  - A report publishes `cards.GetAsync`'s card, which carries a snapshot of the sessions taken outside the
    presence lock.
  - `change-updated` can put back a session that `session-ended` already removed.
  - **Real.** It becomes 9.4, a web-side fix: a card already shown keeps the sessions the live events own.
- **F2** `PresenceStore.cs:110` `Record`:
  - Async hook events are applied in the order they arrive, with no ordering, so `Stop` can lose to a late
    `PostToolUse`, and `SessionEnd` can lose to a late `Stop`.
  - **Real.** It becomes 10.1, an `OccurredAt` event time.
- **F3** `Cli/Hook/HookCommand.cs:235`:
  - A failure after the server held the prompt, but before the wait loop (for example, the preview upload running
    out of time), leaves the prompt answerable for `WaitGrace` (90s) with no one to give the answer to.
  - **Real.** It becomes 10.2, a best-effort `DELETE /api/agent/prompts/{id}`.
- **F4** `PresenceStore.cs:122`:
  - `Record` drops a prompt that's no longer pending on the session's next event. An event that lands between
    `CompleteAnswer` and the hook's next wait call loses the answer: the wait gets a 404, and the terminal asks.
  - **Real but narrow:** `CompleteAnswer` wakes an in-flight wait at once. It becomes 10.2: keep an answered
    prompt until the wait reads it, or `WaitGrace` passes.
- **F5** `PresenceStore.cs:159` `Ask`, and `Remove` at line 371:
  - These release or drop a claimed prompt.
  - **Known trade-off.** `CompleteAnswer`'s comment already says "the record stands". It isn't fixed, and Emmz
    agreed.
- **F6 and F7** `AgentApiClient.cs:81` `ReadAsync`:
  - It wraps only `JsonException`, and the wait loop (`HookCommand.cs:280`) retries only on `BridgeException` and
    `OperationCanceledException`. A body-read `IOException` or `HttpRequestException` makes the hook give up at
    once.
  - **Real.** It becomes 9.3.
- **F8** `Live/ChangeBroadcaster.cs:77`:
  - Each subscriber holds 64 events, dropping the oldest, and session events now add 2 per tool call. A dropped
    `session-ended` or `away-changed` has no resync.
  - **Real.** It becomes 10.3, a `resync` event.

`tasks.md` now has these sections, agreed with Emmz:

- `## 9. Review fixes: small fixes from the review`, with 9.1–9.4;
- `## 10. Review fixes: races in sessions, prompts and the live stream`, with 10.1–10.3.

The release line under "Workflow follow-up" now says to merge `panopticon-skills`' branch and reinstall the plugin
from GitHub.

**Emmz's machine after 8.2:**

- She reinstalled the plugin from GitHub (0.1.0) and restarted.
- She was told to run `! panopticon login --server https://panopticon.team`. I didn't see whether she did. Remind
  her if `/panopticon:setup` still warns about localhost.
- The local server and Docker may still be running from 8.2.

**Gotcha:** auto mode can't check context-mode's `ctx_execute` with a shell script (a hook rewrites its input), and
denied it twice. Run read-only shell scans through Bash instead.

## Next

- **§9, task 9.1:** `web/src/answerList.ts` `answerSummary` should read "Bob allowed Bash for Emmz" and "Bob
  answered a question for Emmz". Update `views/change.test.ts`'s expected rows, and `web/DESIGN.md` "Sessions and
  prompts".
- Then 9.2 → 9.4, the section commit, then §10 (10.1 → 10.3). Context-check after each task.
- After §10:
  - a short whole-change re-review of §9–§10;
  - offer `/code-review` on those fixes;
  - then offer `/opsx:archive`.
- **Follow-up change:** a PWA, so notifications work from any page.
- **Still open with Emmz:** deleting `change/server-invites`, and the CS2012 CLAUDE.md note.
