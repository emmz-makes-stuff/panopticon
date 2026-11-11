# 5.6: the branch names a session's change first

<!-- Section: 5 (task 5.6, added during §6) · Base: c031801 -->

**The problem**, from a side agent's note: `SessionChange.ResolveAsync` took the bridge's `worktrees.json` record
first. So after a switch from a branch that had reported `tags` to `change/projects`, a session's activity and prompts
landed on the `tags` card until the agent reported `projects`.

**Emmz's answers (2026-11-11), over two questions:**

1. She proposed the active OpenSpec change as a fallback after the branch name: the one with the newest `tasks.md`
   when there are several.
2. Asked where the record fits, she chose: branch, then record, then newest `tasks.md`.

**The order now:**

1. A `change/<name>` branch.
2. The bridge's record, but only while its change is still active, meaning `openspec/changes/<name>/tasks.md`
   exists. A stale record is skipped.
3. The active change (never under `archive/`) whose `tasks.md` has the newest write time. A tie goes to the name in
   ordinal order.
4. Otherwise nothing.

**Changed:**

- `Hook/SessionChange.cs` (`NewestActive`, `IsActive`).
- `SessionChangeTests`, rewritten for the new order.
- `HookCommandTests`: the record test now writes the changes' folders.
- The `agent-presence` "A session's change" requirement and its scenarios, with "Switched branch before reporting" and
  "Newest active change" added. The requirement text was cut to stay under `--strict`'s 500-character warning.
- D9's text, with an "Amended during §6" note.
- `docs/cli.md`: "Which change" and the troubleshooting entry.
- `tasks.md`: task 5.6, added and ticked.

**Gates:**

- The full `make gates` run on the code printed `BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0`, `FORMAT_EXIT:0`,
  `WEB_INSTALL_EXIT:0`, `WEB_BUILD_EXIT:0`, `WEB_TEST_EXIT:0`, `WEB_FORMAT_EXIT:0` and `WEB_LINT_EXIT:0`.
- It also printed `VALIDATE_EXIT:1`, for the requirement's length.
- After shortening the requirement's text, `make validate` printed `VALIDATE_EXIT:0`. Only spec text changed after
  the full run.

**Also answered by Emmz, for 6.1** (recorded here, since 6.1 isn't committed yet):

- Session rows use commas, as `web/DESIGN.md` principle 5 asks: "Emmz on mbp: Run make gates, just now". The spec's
  middot wording is to be amended in 6.1.
- Others see "waiting for Emmz" where the session's own member sees "waiting for you".

## Next

- **§6, task 6.1:** session rows on cards.
  - The cards view needs the signed-in member's id, so a row knows who "you" is. Find where `Env` gets the session.
  - Patch the session rows in place, keyed by session id, so a live event doesn't rebuild the card (6.2's prompt form
    must survive).
  - Quiet state comes from `lastActiveAt`. Cards stay ordered by `lastReportedAt`.
  - Amend the `live-dashboard` scenarios "Running gates" and "Waiting since lunch" (and the `remote-answers` "Not away"
    text) to the comma wording.
