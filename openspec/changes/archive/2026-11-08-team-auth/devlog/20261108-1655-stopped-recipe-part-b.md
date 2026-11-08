# Stopped: recipe part A passed; part B needs a Claude Code restart without the bootstrap token

<!-- Section: 9 · Tasks: 9.0, 9.1 ticked; 9.2 unticked · Base: 7ca34bc · Status: stopped, §9 uncommitted on purpose -->

Read `20261108-1645-stopped-human-verification.md` for the full recipe and for what §9 holds. Nothing in the repo has
changed since then.

## Results so far (Emmz, 2026-11-08)

- **Part A:** Emmz said the sign-in process "worked fine". It ran with a real server, and her login token is saved in
  `artifacts/e2e-config`.
  - Still to confirm explicitly: that opening the emailed link in a new tab came back to "Log in <machine>?" (the
    9.0 return trip).
- **Part B, not yet shown.** This Claude Code session called `report_progress` (team-auth §9), and the server took it:
  overall 30/31, section 9 at 2/3.
  - But the card's agent row was `claude-code@Rendles-MacBook-Pro` with **no member**.
  - **Cause:** the session's bridge comes from the gitignored `.mcp.json`. Its env passes
    `PANOPTICON_TOKEN=${PANOPTICON_TOKEN}`, and Emmz's shell has that variable set, probably to the bootstrap token,
    which has no member (D17). An environment variable overrides the saved login (docs/cli.md).
  - The bridge also reads `~/.config/panopticon`, which has no login: the recipe saved hers in
    `artifacts/e2e-config`.
  - So this is expected behaviour, not a bug. `BridgeSettings.NonBlank` treats a blank variable as unset, so an
    unset variable falls back to the saved login.

## Recipe to finish part B

1. Quit `claude`. In the terminal: `unset PANOPTICON_TOKEN`. If the shell profile exports it, remove it there too.
2. `artifacts/cli/panopticon login --server http://localhost:7480`, and approve it in the browser. This saves to the
   default `~/.config/panopticon/config.json`.
3. Start `claude` in this repo, and run `/emmz:pickup`.
4. Claude calls `report_progress` (team-auth, section 9). **Expect** the card's agent row to read
   `claude-code@Rendles-MacBook-Pro` and then Emmz's display name, in grey.

Then run part C (the look), and optionally the revoke check (step 10).

## Next

- **On PASS** (B, C, and the new-tab return trip confirmed): tick 9.2, rerun `make gates`, write the §9-done note,
  and commit `feat(team-auth): integration, README and the return path (§9)`. Then **Finishing a change**.
- **On FAIL:** fix within §9, run `make gates` and `make e2e`, and ask again.
- Afterwards, `rm -rf artifacts/e2e-config`. That login's token can be revoked on the Tokens page.
