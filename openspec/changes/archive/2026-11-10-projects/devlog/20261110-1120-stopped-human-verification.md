# Stopped: §10.2, Emmz's human verification of Projects

<!-- Section: 10 · Tasks: 10.1 done (uncommitted), 10.2 waiting · Base: cf69a83 -->

**10.1 is done but not ticked or committed.** It lands with 10.2 as the one §10 commit.
- **The e2e flow** (`ContainerEndToEndTests`) now also creates a Project (`E2E`) from the browser, then finds its id
  through the bridge's `list_projects`, and writes `.panopticon.json` at the test repo root.
  - **On the card:** `project.code` is `E2E` and `personal` is false.
  - **In the history:** the archived entry still has `E2E`.
- **Results:** `E2E_EXIT:0` (1/1, 40s). The first `make gates` gave `FORMAT_EXIT:2`, because the new `using` lines
  were out of order. With that fixed, the rerun gave `BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0` (583/583),
  `FORMAT_EXIT:0`, `WEB_INSTALL_EXIT:0`, `WEB_BUILD_EXIT:0`, `WEB_TEST_EXIT:0`, `WEB_FORMAT_EXIT:0`, `WEB_LINT_EXIT:0`,
  `VALIDATE_EXIT:0`, `GATES_EXIT:0`. The e2e run came before that fix, which only reordered `using` lines.

## The question

Does Projects work end to end with real people and a real agent? Please run the recipe below, then answer PASS, or
say what failed.

### Setup (from the repo root)

```sh
docker compose up -d --wait
npm --prefix web ci && npm --prefix web run build
make install     # the CLI with list_projects and the link file, into ~/.local/bin
ASPNETCORE_ENVIRONMENT=Development Panopticon__InitialMembers="dev@localhost.test,sam@localhost.test" \
  dotnet run --project src/Panopticon.Server --no-launch-profile
```

Sam is a second initial member here, so they can sign in without an invite. Use two browsers, or one normal window
and one private one:
- **Browser A:** sign in as `dev@localhost.test` ("Emmz").
- **Browser B:** sign in as `sam@localhost.test` ("Sam"). Rename them to Sam on the Members page.

Each sign-in link is in the server log's "Development: not sending email" warning.

### 1. Two people, two Projects, live

1. **A:** create the Project `Panopticon` / `PAN`. **B:** create `Acme` / `ACME`.
2. Report a change into each, from two scratch repos. Each reporter needs its own login:
   ```sh
   # Emmz: the normal login. Then link a repo to PAN (copy the file from PAN's page) and report.
   panopticon login --server http://localhost:7480
   # Sam: a separate config directory, approved in browser B.
   XDG_CONFIG_HOME="$HOME/.panopticon-sam" panopticon login --server http://localhost:7480
   ```
   The simplest way to report is a real agent in each repo (part 2). Run Sam's with `XDG_CONFIG_HOME` set the same
   way.
3. **Check:**
   - **The dashboards:** A shows only PAN's card, and B only ACME's. Each updates live as its agent reports, and
     neither sees the other's.
   - **Archiving:** archive one change in each. Each archive is in its owner's History only, and the Metrics count
     only your own Project's.
   - **Opening the other's change:** paste one browser's change-page address into the other browser. It says it
     wasn't found.
   - **The filters:** the Project filter on each page offers only your own Projects.

### 2. A real agent links and reports

In a scratch repo with an OpenSpec change (or this repo, whose `.panopticon.json` points at your real Project), start
Claude Code with `panopticon mcp` connected (`claude mcp add panopticon --scope local -- panopticon mcp`). Then ask:

> List my Panopticon Projects, link this repo to PAN by writing .panopticon.json, then report progress on <change>.

**Check:**
- **The calls:** it calls `list_projects`, then writes a file with PAN's id.
- **The card:** its next report's card shows the `PAN` plate.
- **A personal change first:** if the change was already reported without the file, its personal card moves into
  PAN rather than duplicating.

### 3. Members, while the other person watches

Keep B's dashboard open throughout, without reloading:

1. **A:** on PAN's page, invite `sam@localhost.test` as a viewer. Wait about 30s (the stream's keep-alive is 25s),
   then have Emmz's agent report on PAN's change. **B:** its card appears live. (A reload shows every PAN card.)
2. **A:** change Sam to editor, then to owner, and check the status line. Then make yourself an editor: it asks
   first. Choose OK, and it works now, because Sam is an owner. Then have Sam make you an owner again.
3. **The last owner:** make Sam a viewer again, then on your own row pick Editor, and choose **Leave this
   Project**. Both say "A Project needs an owner. Make someone else an owner first."
4. **A:** remove Sam from PAN. **B:** after about 30s, PAN's reports no longer reach B's dashboard live. A reload
   shows no PAN cards, and Sam's agent's next report into PAN is refused.
5. **The Members page:** as Emmz (an initial member) you see Remove; as a member who isn't an initial member you
   wouldn't. There's no invite form, just the line pointing to Projects.

### 4. Look and feel

Check the Projects list, a Project's page, and the code plate on cards, a change page and history rows:
- in light and dark mode;
- on desktop, and at phone width (about 375px, in the browser's device toolbar);
- with one card idle or stale: its plate dims with the rest of the card.

**On PASS:** tick 10.1 and 10.2, write the §10-done note, and make the §10 commit. Then the whole-change review
(`git diff 5b9a8a2..HEAD`: from the proposal commit, before §1 `ee98ad3`), the change-done note, and an offer of `/code-review`
before `/opsx:archive`.

**On FAIL:** add a "Review fixes" section to `tasks.md` for what failed, agree it with Emmz, and build it with the
section loop.

## Next

- Wait for Emmz's PASS, or her list of failures, from the recipe above.
