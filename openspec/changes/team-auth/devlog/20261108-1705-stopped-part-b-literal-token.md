# Stopped: part B failed on a literal `${PANOPTICON_TOKEN}`; fixed in §9, rerun needed

<!-- Section: 9 · Tasks: 9.0, 9.1 ticked; 9.2 unticked · Base: 7ca34bc · Status: stopped, §9 uncommitted on purpose -->

The full recipe and what §9 holds are in `20261108-1645-stopped-human-verification.md`. Part A's result is in
`20261108-1655-stopped-recipe-part-b.md`.

## What happened

- Emmz restarted `claude` with `PANOPTICON_TOKEN` unset and a fresh `panopticon login` (saved 16:56). In the new
  session, `report_progress` (team-auth, §9) got `rejected the token (401)`.
- **Cause:** the bridge process env (from `ps eww`) held `PANOPTICON_TOKEN=${PANOPTICON_TOKEN}` **literally**. Claude
  Code doesn't expand `${VAR}` from `.mcp.json` to blank when VAR is unset. It passes the text through. That's
  non-blank, so it overrode the saved login (as the spec says it should), and the server rejected it.
- `docs/cli.md` recommended exactly that `"${PANOPTICON_TOKEN}"` form. The 401 message said "Run 'panopticon login'",
  which can't help while an env token is set.

## Decision (Emmz, chose "Docs + clearer 401")

- `docs/cli.md`: you don't need an `env` block with `panopticon login`. If you use one, write
  `"${PANOPTICON_TOKEN:-}"` and say why. Troubleshooting now lists the two 401 messages separately.
- The bridge records where its token came from (`BridgeSettings.TokenFromEnvironment`). `ServerForwarder` names that
  source in the 401/403 error:
  - Env: "rejected the token from PANOPTICON_TOKEN … overrides the saved login: correct it, or remove it …".
  - Saved login: "rejected the saved login's token …: it was revoked, or its member was removed. Run
    'panopticon login'".
- Tests: `BridgeTests.Token_rejected_says_so_without_echoing_the_token` (env case) and the new
  `Saved_login_token_rejected_says_to_log_in_again`, plus `CliConfigTests.The_token_records_whether_it_came_from_the_environment`.
- **Rejected:** treating an unexpanded `${…}` value as unset in the bridge. It works around one client's quirk in
  product code. No spec change was needed: the error wording isn't in the spec.
- Emmz's gitignored `.mcp.json` is changed to `"PANOPTICON_TOKEN": "${PANOPTICON_TOKEN:-}"` (her OK).

## Gates

- `make gates`: `BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0`, `FORMAT_EXIT:0`, `WEB_INSTALL_EXIT:0`, `WEB_BUILD_EXIT:0`,
  `WEB_TEST_EXIT:0`, `WEB_FORMAT_EXIT:0`, `WEB_LINT_EXIT:0`, `VALIDATE_EXIT:0`, `GATES_EXIT:0`
- `make e2e`: `AOT_EXIT:0`, `E2E_EXIT:0`

## Recipe to rerun part B

1. Quit `claude`. The CLI binary in `artifacts/cli` was republished, and `.mcp.json` changed. Keep `PANOPTICON_TOKEN`
   unset. The server must still be the one at `http://localhost:7480` that issued the 16:56 login.
2. Start `claude` in this repo and run `/emmz:pickup`.
3. Claude calls `report_progress` (team-auth, section 9). **Expect** the card's agent row to read
   `claude-code@Rendles-MacBook-Pro` and then Emmz's display name, in grey.
   - If it fails with "rejected the saved login's token", the server's database was reset: rerun
     `artifacts/cli/panopticon login --server http://localhost:7480`.

Then part C (the look), the 9.0 new-tab return trip ("Log in <machine>?"), and optionally the revoke check (step 10)
from the 1645 note.

## Next

- **On PASS** (B, C, and the new-tab return trip confirmed): tick 9.2, rerun `make gates`, write the §9-done note, and
  commit `feat(team-auth): integration, README and the return path (§9)`. Its body notes the 401-source fix and the
  `docs/cli.md` change. Then **Finishing a change**.
- **On FAIL:** fix within §9, run `make gates` and `make e2e`, and ask again.
- Afterwards: `rm -rf artifacts/e2e-config`. That login's token can be revoked on the Tokens page.
