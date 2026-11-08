# Stopped: §7.4 needs Emmz to watch live updates in a real browser

<!-- Section: 7 · Tasks: 7.1–7.4 · Base: 1b7cd82 · Status: stopped, §7 uncommitted on purpose -->

**Why stopped:** CLAUDE.md's "Stop and ask" says live updates seen in a real browser, and the real
dashboard's look and feel, need human verification. 7.1–7.3 are ticked, and 7.4 isn't. **All of §7 is
uncommitted**, plus `.gitignore` (`.mcp.json`).

**Question for Emmz:** do the live updates (and the built dashboard) work and look right in a real
browser? Recipe below.

## What landed (uncommitted)

- **7.2, in `web/src`:**
  - `types.ts` mirrors the `/api/changes` JSON.
  - `api.ts` has `fetchChanges` with an injectable fetch.
  - `format.ts` has `percent`, `countText` (`2/5 (40%)`), `agentName` (`client@machine`) and `relativeTime`.
  - `progressBar.ts` builds lit-cell bars: one cell per task, merged into a continuous fill above 40 tasks,
    and an empty state with no tasks.
  - `card.ts` builds the card with `textContent` only, which is tested against HTML injection.
  - `app.ts` has the header with the "a PANOPTICON" wordmark, the grid, the empty state (`claude mcp add`
    with `location.origin`), and the load-error alert.
  - Fonts are self-hosted via `@fontsource/gabarito` and `@fontsource/atkinson-hyperlegible-next` 5.3.0
    (exact pins, under `dependencies`). `styles.css` ports the approved preview.
  - **Spec wording wins over the preview copy.** Counts read `2/5 (40%)` and agents `claude-code@vm-1`, as
    the live-dashboard scenarios word them. `DESIGN.md` was updated to match.
- **7.3:**
  - Each bar's cells are `role="progressbar"`, with `aria-label` "Section N progress" or "Whole change
    progress", and `aria-valuemin` 0, `aria-valuemax` total and `aria-valuenow` done.
  - `aria-valuetext` reads "2 of 5 tasks, 40 percent".
  - Empty bars have no progressbar, only their text.
- **7.4:**
  - **`App`** gains `upsert` (newer-wins, in place, moving the card to the top), a merging `render`
    (a card an event made newer survives a refresh), `refresh`, `setStatus` (Connecting… / Live / Offline,
    reconnecting… in a `role="status"`) and `updateTimes`.
  - **`live.ts` `connectLive`:**
    - `EventSource('/api/events')`. On every `open`: `refresh()`, then Live (or offline if the refresh
      fails).
    - `change-updated` → `upsert`. Malformed data is ignored.
    - `error` → offline. If `readyState` is CLOSED, it reopens after a back-off (1, 2, 5, then 10 s,
      reset on success).
    - Returns `stop`.
  - The one motion: newly lit cells get `.lighting`, staggered 60 ms, and are switched off by
    `prefers-reduced-motion`. A just-updated card gets the `--spark` left edge.
  - Tests use a `FakeEventSource` in `testing.ts`.
- **Self-review addition:** "last reported" times now re-render every 30 s (`updateTimes`, from
  `main.ts`). Otherwise an open dashboard says "just now" forever, which defeats "stalls spotted fast".
- **Server fix (a §5 bug found here):** `GET /api/events` didn't send headers until the first event, so
  a browser's `EventSource` never fired `open` (stuck on "Connecting…", no refresh-on-open).
  - `StreamAsync` now takes a `subscribed` callback. The endpoint passes one that runs
    `Response.StartAsync` **then** `Body.FlushAsync`; `StartAsync` alone doesn't put headers on the wire.
  - It subscribes *before* sending headers, so the client's refresh-on-open can't miss an event.
  - New test: `Events_send_headers_at_once_without_waiting_for_a_report`.
  - Raw `node:http` against Kestrel: headers in 11 ms.
  - Gotcha: undici `fetch` via context-mode seemed to hold them back until the timeout. Use `node:http`
    to probe SSE.
- **Deferred:**
  - Stale/idle dimming (DESIGN.md has it, but it isn't in this change's spec, and the threshold is an
    open DISCOVERY question).
  - SSE heartbeat.

**Gates:** `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` (121) `FORMAT_EXIT:0` `WEB_INSTALL_EXIT:0`
`WEB_BUILD_EXIT:0` `WEB_TEST_EXIT:0` (35) `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0` `VALIDATE_EXIT:0`
`GATES_EXIT:0`.

**Server state:** the dev server (with the SSE fix) is running on 7480 from this session, and compose is
up. Stop it by PID only.

## Recipe (Emmz + Claude together)

1. Open <http://localhost:7480>. Expect the "a PANOPTICON" wordmark, one `walking-skeleton` card, and
   "Live" after a moment. Check both light and dark (system setting).
2. Tell Claude "go". Claude calls `report_progress` (`walking-skeleton`, section 7). The card should
   switch to "Section 7: Web dashboard" 3/4. The whole-change bar should go from 22/30 to 26/30, its four
   new cells lighting in sequence (the section bar doesn't animate when the section changes). The card
   should get the pink edge, and there's no reload.
3. Claude stops the server for about 15 s. The header should read "Offline, reconnecting…". Claude
   restarts it, and the header should return to "Live" with the cards refreshed.

## Next

- **On PASS:** tick 7.4, then write the §7 section-done note (fold in this note and
  `20261108-1334-design-approved.md`). Commit `feat(walking-skeleton): web dashboard (§7)` with lines
  7.1–7.4, mentioning the SSE header fix and `.gitignore` in the body. Run the context check. §7 is the
  last section, so go on to **Finishing a change**: whole-change self-review
  (`git diff 7d3feb6^..HEAD`), the change-done note, then offer `code-review`.
- **On FAIL:** fix within §7, rerun `make gates`, and re-check.
