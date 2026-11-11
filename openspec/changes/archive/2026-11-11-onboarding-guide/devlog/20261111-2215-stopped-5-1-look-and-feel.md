# §4 done; stopped for 5.1 (look and feel in a real browser)

<!-- Section: 4 done (4.1), 5 open · Base for §4: fcf8358 -->

## §4 done

- `README.md` "Reporting progress": rewritten around the guide's three steps. The plugin commands are under step 3,
  linking is mentioned last, and the `.mcp.json` route links to `docs/cli.md#claude-code`.
- `docs/cli.md`: "Install" mentions the guide. "Claude Code" leads with the plugin, what it does, and a warning not
  to add a `panopticon` server to `.mcp.json` as well (the plugin's `/panopticon:setup` checks for that duplicate).
  The `.mcp.json` and `claude mcp add` routes follow under **Without the plugin**.
- Checked: the three plugin commands match `PLUGIN_COMMANDS` exactly in `README.md`, `docs/cli.md` and
  `setupGuide.ts`. `MCP_JSON` equals the `docs/cli.md` block (diffed).
- Gates: `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` `FORMAT_EXIT:0` `WEB_INSTALL_EXIT:0` `WEB_BUILD_EXIT:0`
  `WEB_TEST_EXIT:0` `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0` `VALIDATE_EXIT:0` `GATES_EXIT:0`

## The question for Emmz (5.1)

Run these from the repo root, on `change/onboarding-guide`. `cli-dist/` still holds the `linux-arm64` 0.0.0-dev build
from `make e2e`, so the install step shows real commands.

```sh
docker compose up -d --wait
```

```sh
npm --prefix web ci && npm --prefix web run build
```

```sh
export Panopticon__CliDirectory="$PWD/cli-dist"
```

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

Open <http://localhost:7480> and sign in as `dev@localhost.test` (the link is in the server log). Then check:

1. **Projects:** the "Set up a machine" guide sits above the list, with no step done. Check that it looks right on
   desktop and at phone width (DevTools, about 375px).
2. **Tokens:** the same guide below the token list. Check:
   - the OS tabs and Copy work;
   - each plugin command and the log-in command copies;
   - "Other agents" opens to the `.mcp.json`.
3. **Hide** on Projects removes the guide, and a reload keeps it hidden. Tokens still shows it. To bring it back,
   run `localStorage.removeItem("panopticon.setupGuide.hidden")` in the console.
4. **Steps marked done.** In a second terminal, use a **throwaway** config, so your real panopticon.team login isn't
   overwritten:

   ```sh
   export XDG_CONFIG_HOME="$TMPDIR/panopticon-5-1"
   ```

   ```sh
   panopticon login --server http://localhost:7480
   ```

   Approve it, then reload Tokens: Install and Log in show as done, and Connect doesn't.
   - Optional: from that same terminal, start `claude` in a repo with the plugin, and ask it to report progress.
     After a reload, Connect is done too, and the guide leaves Projects. The unit tests already cover this case.
5. **A Project's page:** under the link file's Copy row is the muted line "Or ask your agent to link this repo to
   <CODE>: …".

Reply PASS, or say what to change: spacing, the cells, wording or placement.

On PASS: tick 5.1, and record 5.2 as waiting for the next release (or run it after one). Then do the whole-change
review and offer `code-review`.
On FAIL: record what to change as a fix section, or as edits in §2/§3 if it's small, agreed with Emmz.

## Update: "the guide isn't on the Projects page"

Emmz saw no guide on Projects. I checked in the browser:
- the new build is loaded (the `.projects-setup` slot is there);
- Hide isn't set;
- `/api/tokens` returned 200 with one token, `Rendles-MacBook-Pro`, which last reported 19h ago (local dev data).

So the rule worked as specified. Emmz chose to **keep the rule**: she'll revoke that local dev token on the Tokens
page and reload Projects to see the guide, then finish the 5.1 checks.

On the Tokens page, the guide rendered as designed in dark mode: the done cells, "Done", and the rail.

## Next

- Wait for Emmz's 5.1 result (after her revoke).
- Notes for change-done: the flaky `ScopedStreamTests` (see the §2 note), and that 5.2 needs a release.
