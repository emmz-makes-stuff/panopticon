# Stopped: 9.2 needs Emmz for real email, a real browser, and Claude Code end to end

<!-- Section: 9 · Tasks: 9.0, 9.1 done (ticked); 9.2 built, unticked · Base: 7ca34bc · Status: stopped, §9 uncommitted on purpose -->

(File-name time: set to sort after `20261108-1640`. The real clock read 16:38 UTC.)

**Why stopped:** the rest of 9.2 is human verification. It needs a Resend account and a verified domain, a look
in a real browser, and a real Claude Code reporting through a `panopticon login` token. **§9 is all uncommitted**,
and it commits once, after the recipe passes.

## What's done (uncommitted)

- **9.0, ticked:** the return path moved to localStorage. See `20261108-1640-return-path-localstorage.md`.
- **9.1, ticked:** `tests/Panopticon.Cli.IntegrationTests/ContainerEndToEndTests.cs` is an opt-in xUnit test
  (Emmz's choice).
  - Setup:
    - It builds the image from the `Dockerfile` with Testcontainers (a unique tag, deleted afterwards).
    - On a private network it starts Postgres 17 and SeaweedFS, creates the bucket from the host, runs the image
      once with `--migrate`, then serves it on a free host port, with `PublicUrl` set to that port.
    - It runs in Development with no Resend key, so emails are logged.
  - Steps:
    1. It signs in as the initial member `e2e@example.test` through the link read from `docker logs`.
    2. It runs the published `panopticon login --server …` with `XDG_CONFIG_HOME` in a temp dir, and approves
       it over HTTP.
    3. It reports through the published `panopticon mcp`, with `PANOPTICON_*` removed from its environment, so
       only the saved login is used. A null value removes a variable: the MCP SDK docs say so.
    4. It checks that the card's agent is `e2e-agent` with member `e2e`.
  - On failure it writes the server log to the test output.
  - `[Trait("Category", "ContainerE2E")]`. `make test` now passes `--filter-not-trait "Category=ContainerE2E"`,
    and the new **`make e2e`** target (`E2E_EXIT`) runs only this test. CLAUDE.md and the README say so.
  - Its first run took 1 m 14 s with Docker's layer cache warm. A rerun took 26 s.
  - `RepoRoot.Path` was moved out of `PublishedCli`, since both need it.
- **9.2, built but unticked:** the README covers all of this:
  - the new intro;
  - a Development warning, which replaces "local use only until sign-in lands";
  - "The first sign-in", where the link is logged in Development;
  - "Reporting progress" with `panopticon login`;
  - `Panopticon:Resend:ApiKey` and `Panopticon:Email:From` in the configuration table, and `PublicUrl` as required
    outside Development;
  - sign-in in the container section;
  - `make e2e`.

## Gates

`BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0` (305 passed, 0 failed), `FORMAT_EXIT:0`, `WEB_BUILD_EXIT:0`,
`WEB_TEST_EXIT:0` (96 passed), `WEB_FORMAT_EXIT:0`, `WEB_LINT_EXIT:0`, `VALIDATE_EXIT:0`, `GATES_EXIT:0`.
`E2E_EXIT:0` (1 passed).

## Recipe

Start from the repo root with `docker compose up -d --wait`, `npm --prefix web ci && npm --prefix web run build`,
and `make aot`. Use **your real address** for `<you>`.

**A. Real email, and the new-tab return trip**

1. Start the server in Development, but with a Resend key, so it really sends:
   ```sh
   ASPNETCORE_ENVIRONMENT=Development \
   Panopticon__Resend__ApiKey='re_…' \
   Panopticon__Email__From='a PANOPTICON <sign-in@your-verified-domain>' \
   Panopticon__InitialMembers='<you>' \
   dotnet run --project src/Panopticon.Server --no-launch-profile
   ```
2. In another terminal: `XDG_CONFIG_HOME="$PWD/artifacts/e2e-config" artifacts/cli/panopticon login --server http://localhost:7480`.
   It prints a code and a `/device?code=…` link. Leave it waiting.
3. In a browser that isn't signed in (or after Sign out), open that `/device?code=…` link. **Expect** the sign-in
   page.
4. Enter `<you>` and choose **Send me a link**. **Expect** "Check your email", and an email within a minute. It
   should be plain, from your From address, with a link ending `/sign-in/confirm#…`.
5. Open the link **from your mail app**, so it opens a new tab. **Expect** "Sign in as `<you>`?" and one
   **Sign in** button. Opening the page alone doesn't sign you in.
6. Choose **Sign in**. **Expect** to land on "Log in `<your machine>`?" with the same code as the terminal.
   That's the return trip 9.0 fixed.
7. Choose **Approve**. **Expect** the CLI to finish within about 5 s, saying it logged in as you.

**B. Claude Code end to end, with that token**

8. In a repo with an OpenSpec change, in a shell where `PANOPTICON_TOKEN` and `PANOPTICON_SERVER_URL` are
   **unset**:
   `claude mcp add panopticon --scope local -e XDG_CONFIG_HOME="<repo>/artifacts/e2e-config" -- "<repo>/artifacts/cli/panopticon" mcp`.
   Then start `claude`, and ask it to report progress on the change.
9. **Expect** the card on the open dashboard to appear or update live, with the agent row reading
   `claude-code@<machine>` followed by your name in a quieter grey.
10. Optional: on **Tokens**, revoke that machine's token, and ask Claude Code to report again. **Expect** a tool
    error saying the token was rejected.

**C. Look and feel**

11. Look at the header and at the Sign in, Check your email, confirm, Members (invite, rename, remove someone),
    Tokens and device pages, in light and dark (OS setting), and at phone width (devtools, 375 px).
    **Expect**:
    - the wordmark unchanged, the live status on the right, and your name, Members, Tokens and Sign out last;
    - one left-aligned column, with rows between hairlines;
    - ink-coloured main buttons, and no teal outside the progress bars;
    - the device code as the largest text on its page;
    - nothing that scrolls sideways on a phone.
12. Stop the server. `rm -rf artifacts/e2e-config`, and `claude mcp remove panopticon --scope local` in the test
    repo.

## Next

- **On PASS** (Emmz says so, with any copy or look notes):
  - Tick 9.2, rerun `make gates`, write the §9-done note, and commit
    `feat(team-auth): integration, README and the return path (§9)` with bullets for 9.0–9.2.
  - Then **Finishing a change**: the whole-change self-review from §1's base, the change-done note, and an offer of
    `code-review`.
  - Check `tasks.md` "## Workflow follow-up" first.
- **On FAIL:** fix within §9, adding a task if it's new work, run `make gates` and `make e2e`, and ask for the
  failed step again.
- Nothing is running. The branch is unpushed, with no upstream.
