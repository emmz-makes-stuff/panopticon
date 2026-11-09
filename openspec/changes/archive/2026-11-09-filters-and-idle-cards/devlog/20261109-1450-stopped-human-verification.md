# Stopped: 7.2, human verification

§1–§6 are committed (head `f44ead8`). 7.1 is done: `make gates` gave GATES_EXIT:0 at `f44ead8` (all labels 0), and `make e2e` gave E2E_EXIT:0 (1 test). 7.1 is ticked in the working tree, uncommitted, as the stop rule says. §7 commits after Emmz passes the recipe.

## What's blocked

7.2 needs Emmz's eye in a real browser:
- the filter bar and the quiet states, light and dark, at desktop and phone width;
- live filtering while an agent reports;
- a card going idle;
- a shared link opened in another browser;
- the history's filters and "Show more".

The design choices to judge are in `…-section-6-design.md`: stale without opacity (dashed edge, grey cells), the comma instead of the middot, and the selects pairing up on a phone with no toggle.

## Next

When Emmz says it passed:
1. tick 7.2, write the section-done note, and commit §7;
2. run the whole-change review (`git diff d3e809b..HEAD`);
3. write the change-done note, offer `/code-review`, then offer `/opsx:archive`.

If she wants changes, add them to `tasks.md` as a new section and build them with the loop.
