# Tasks

## 1. Server: quiet thresholds and member ids on cards

- [x] 1.1 Add `ActivityOptions` (`Panopticon:Activity`: `IdleAfter` 30 min, `StaleAfter` 4 h), bound and validated on start: both must be positive, and `StaleAfter` must exceed `IdleAfter`, with an error naming both keys (D1). Verify the "Default thresholds" and "Thresholds out of order" scenarios with server tests.
- [x] 1.2 Add `GET /api/settings` (member-only), returning `idleAfterSeconds` and `staleAfterSeconds`. Verify it with an endpoint test, including a 401 when signed out.
- [x] 1.3 Add `MemberId` to `CardAgent`, set in `ChangeCards.ToCard` (D6). Verify that `/api/changes`, a `change-updated` event and `/api/history` all carry `memberId`, and that it's null for a bootstrap-token agent.
- [x] 1.4 Document `Panopticon:Activity:IdleAfter` and `StaleAfter` in the README's configuration table, and verify that the setting names match the options section.

## 2. Server: history filters

- [x] 2.1 Accept `repo`, `owner`, `member`, `machine` and `client` on `GET /api/history`, applied in `ChangePages.HistoryAsync` against the `…Key` columns and `Agents.Any` (D3, D4). Malformed values return 400 naming the parameter. Verify each filter scenario in change-filters against the API: one repo, one owner, owner with a subgroup, person, shared change, machine, agent type, owner and person combined, and case-insensitive matching.
- [x] 2.2 Verify "Filtered paging": seed 60 archived changes in one repo and 40 elsewhere. The first filtered page holds 50, `nextBefore` continues with the other 10, and a run of equal archive times still never splits a page.
- [x] 2.3 Add `GET /api/history/filters` (member-only): the distinct repos, owners, members (id and display name), machines and clients over archived changes only (D5). Verify "History choices" (active-only values are absent), that a no-member agent adds no person, and a 401 when signed out.

## 3. Web: quiet cards

- [x] 3.1 Fetch `/api/settings` once when the signed-in shell starts, and pass the thresholds to views in `Env`. Verify with a shell test that the settings reach the cards view, and that a failed settings fetch falls back to the defaults with a console warning, not a broken page.
- [x] 3.2 Add `quietState(lastReportedAt, now, settings)` (D2). Verify with unit tests at the boundaries (just under and just over each threshold, and a future timestamp is active).
- [x] 3.3 Render the state on each card: the `card--idle` and `card--stale` classes, an "Idle" label, and "Stale · last seen 5h ago" (D2). Re-evaluate on the 30 s tick, and make a card active again on a new report. Verify "Recently reported", "Quiet for an hour", "Quiet for a morning", "Goes idle while watching" (a fake clock moved past 30 min, then one tick) and "Report wakes a stale card" in `app`/`card` tests.

## 4. Web: the filter model and the dashboard's filters

- [x] 4.1 Add `filters.ts`: `parseFilters(params)`, `toQuery(filters)` (fixed key order) and `matches(card, filters, state)` (D4). Verify with unit tests: repo and owner parsing (including subgroups and malformed values), case-insensitive matching, combining filters, and a round trip of parse and `toQuery`.
- [x] 4.2 Add `filterChoices(cards)` (D5): distinct sorted repos, owners, members by id (latest display name), machines and clients, with a chosen value kept even when no card has it. Verify with unit tests.
- [x] 4.3 The dashboard keeps the full card set and shows the filtered subset. Live upserts and removals, the 30 s tick (for status) and filter changes all re-evaluate it. The count reads "3 of 7 changes" when filters hide some, and a filtered view with no matches says so, with a clear button. Verify "Report outside the filter", "A new agent joins a change", "A card goes stale while filtered", "A filter value that matches nothing" and "Dashboard choices follow the cards" in `app` tests.
- [x] 4.4 Add the filter bar component (selects per dimension, "All …" options, labels, and "Clear filters"), and wire it to the dashboard. The cards view returns a `ViewHandle` whose `update(params)` re-filters in place (D4). Verify with view tests: choosing a filter writes the address in key order without a remount, refetch or reconnect; Back restores the previous filters ("Back undoes a filter"); opening a filtered address shows it filtered ("Share a filtered dashboard"); and "Clear all".

## 5. Web: the history's filters

- [x] 5.1 The history view reads filters from the address, fetches `/api/history/filters` for the choices, and calls `/api/history` with the filter query. "Show more" keeps the filters, and a filter change resets the list in place through `update(params)`. There's no status filter here. Verify with history view tests: a filtered first page, "Show more" within the filter, a filter change resets and refetches, the empty filtered state, and a failed choices fetch still shows the list.

## 6. Design pass

- [x] 6.1 Run the `frontend-design` skill for the filter bar (desktop, and phone collapsing or wrapping) and the quiet states (idle muted, stale dimmed, the lit cells still readable), within `web/DESIGN.md`. Add a "Filters and quiet cards" section to `web/DESIGN.md`, and apply the styles. Verify that `make web-build` and `make web-lint` pass, and record the choices in a devlog note.

## 7. Integration and human verification

- [ ] 7.1 Run `make gates` and `make e2e` (the image's new settings endpoint and options), and verify `GATES_EXIT:0` and `E2E_EXIT:0`.
- [ ] 7.2 Write the human verification recipe in a devlog note, then stop for Emmz to run it:
  - the filter bar and the quiet states, in light and dark, at desktop and phone width;
  - filtering the live dashboard while an agent reports, including a change outside the filter;
  - a card going idle in a real browser (with `IdleAfter` set to 1 minute locally);
  - a shared filtered link opened in another browser;
  - filtering the history and "Show more".

## Workflow follow-up

- Run the whole-change review and offer `/code-review` before archiving.
- Archive with `/opsx:archive` once the review is done.
- Later changes from the roadmap: metrics, then `panopticon setup`, Releases and the Railway deploy (forwarded headers, a CSP header).
