# Session rows: the activity shows, and who and where go in the time's tooltip

<!-- Section: 6 · Task: 6.4 (sign-off feedback) · Base: 4aa8b0b -->

During the 6.4 check, Emmz asked for the activity to be bigger, the elapsed time to stay small and grey, and "dev on
Rendles-MacBook-Pro" with the full timestamp to move into a tooltip on the time.

**Her answers (2026-11-11):**

- Amend the spec. In `live-dashboard`, "Sessions on a card" now puts the member, the machine and the full time in
  the elapsed time's tooltip, and in the text a screen reader reads. "Running gates" now expects "Run make gates,
  just now", with "Emmz on mbp" and the time in the tooltip.
- The tooltip is a `title`, and screen readers get hidden text (`.visually-hidden`). Touch users don't get the
  tooltip.
- The activity is body size, 16px, in ink. The time is `--step--1`, muted.

**Built:**

- `sessionRows.ts` `renderRow`:
  - `.card-session-who` is visually hidden and reads "Emmz on mbp: ".
  - The comma is hidden too.
  - The time's `title` is "Emmz on mbp, <locale timestamp>".
- The row's `textContent` hasn't changed, so the existing tests still read "Emmz on mbp: Run make gates, just now".
- A new test in `sessionRows.test.ts` covers the hidden text and the tooltip. `web/DESIGN.md` "Sessions and
  prompts" is updated.

**Gates:** `WEB_TEST_EXIT` was not run through make. `npx vitest run` passed 524 tests. `WEB_LINT_EXIT:0`,
`WEB_FORMAT_EXIT:0`, `WEB_BUILD_EXIT:0` and `VALIDATE_EXIT:0`.

**Trade-off Emmz accepted:** two sessions on one card look alike until you hover over their times.

## Then: a `❯` terminal mark

Emmz asked for a `❯` before each session row's text, in an accent colour, to mark it as a line from a terminal.

**Her answers (2026-11-11):**

- A new token, `--prompt`: `#6E56CF` light and `#A594F9` dark. It has no other use, so teal stays progress only
  and spark stays live and attention.
- Contrast: 5.39:1 on the light card, 4.99 on paper, 6.28 on the dark card and 7.02 on paper.
- Every session row gets the mark, working or waiting.

**Built:**

- `.card-session-what::before`, with `content: "❯" / ""`. The alt text is empty, so screen readers skip it, and
  plain `content: "❯"` comes first as a fallback.
- Because the mark is CSS only, the row's `textContent` and the tests are unchanged.
- `web/DESIGN.md` adds the token's row and updates "Sessions and prompts" and its wireframe.

**Gates:** `WEB_TEST_EXIT:0`, `WEB_LINT_EXIT:0`, `WEB_FORMAT_EXIT:0` and `WEB_BUILD_EXIT:0`.

## Next

- Still the 6.4 stop in `20261111-2039-stopped-section-6-sign-off.md`. Waiting on Emmz:
  - the look, now including this row change;
  - notifications only firing on the cards page;
  - "(2) PANOPTICON" in the tab-title scenario.
- Then tick 6.1–6.4, write the section-done note, and make the §6 commit.
