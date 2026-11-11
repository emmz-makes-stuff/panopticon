# Stopped: §8.1 green, and §8.2 needs a real Claude Code session

<!-- Section: 8 · Tasks: 8.1 built (unticked), 8.2 pending · Base: 462d17e -->

§8.1 is **uncommitted on purpose**. Section 8 commits once, after Emmz's 8.2 check. Don't revert it.

## 8.1, built

`tests/Panopticon.Cli.IntegrationTests/ContainerEndToEndTests.cs` gains step 6a–6e. It runs after the change page
and before the archive, against the real image:

- **6a:** a `PreToolUse` Bash event through the published `panopticon hook`, on stdin, gives exit 0 and empty
  stdout. The card shows a working session reading "Run make gates", with the member.
- **6b:**
  - `PUT /api/me/away {away:true}`.
  - An `AskUserQuestion` hook starts and blocks. The card's session has a held `promptDetail`, and the option's
    preview is rendered on the server (`<strong>durable</strong>`).
- **6c:**
  - The member, who owns the Project, posts the answer "Postgres".
  - The hook exits 0, printing `PreToolUse`, `allow`, `answers["Which store?"] = "Postgres"` and "Answered by <name>
    via Panopticon.".
- **6d:**
  - Read with `PostgresFixture.CreateDb`, there's one `remote_answers` row: the change, the session's member and
    who answered, kind `question`, and no tool or outcome.
  - Its `QuestionsJson` has "Postgres" and not the preview.
  - The change page's `Answers` lists it.
- **6e:** away is turned off, a `SessionEnd` hook runs, and the session leaves the card.

New helpers: `CardAsync`, `HookEvent` (as in the CLI integration tests) and `RunHookAsync` (stdin, cwd, an isolated
config, no env token).

**Gates:**

- `E2E_EXIT:0`: 1 test passed in 39s.
- `GATES_EXIT:0`: `BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0`, `FORMAT_EXIT:0`, `WEB_BUILD_EXIT:0`,
  `WEB_TEST_EXIT:0`, `WEB_FORMAT_EXIT:0`, `WEB_LINT_EXIT:0` and `VALIDATE_EXIT:0`.

## 8.2, for Emmz: the recipe

**Setup:**

1. Start the local server, as in the README:
   ```sh
   cd ~/github/emmz/panopticon
   docker compose up -d --wait
   npm --prefix web run build
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
   ```
2. In a second terminal, install the CLI and log it in:
   ```sh
   cd ~/github/emmz/panopticon && make install && panopticon login --server http://localhost:7480
   ```
3. In Claude Code, swap in the local plugin. `panopticon-skills` is on its `change/agent-interaction` branch.
   ```text
   /plugin marketplace remove panopticon-skills
   /plugin marketplace add ~/github/emmz/panopticon-skills
   /plugin install panopticon@panopticon-skills
   ```
   Then quit and restart `claude` in `~/github/emmz/panopticon`. `/panopticon:setup` should pass, step 6 included.
4. For the "Project owner" answer:
   - Make `sam@localhost.test` an **owner** of the Project this repo is linked to (PAN), from its Project page.
   - Sign in as Sam in a private window.

**Checks:**

1. **Activity:** ask the agent to run `make validate`. Your card shows "❯ Validate …" (its description) live, then
   "waiting for you" when its turn ends.
2. **Away on:** turn it on in the header, so the pill reads "Away on".
3. **A question, answered by you:** ask the agent to use AskUserQuestion with a multi-select question, "Which areas
   does this touch?", with the options Hooks, Web and CLI.
   - The terminal asks nothing.
   - **The card shows exactly one "Needs you"** (the matcher check from `20261111-2150-plugin-hooks.md`). Two would
     mean the async `PreToolUse` caught `AskUserQuestion`.
   - Pick Hooks and Web, then Send answer. The agent gets "Hooks, Web", answered by you.
4. **A permission prompt, answered by Sam:** ask the agent to create `away-test.txt` containing "hello".
   - Sam's card shows "Needs you", with Write and its input.
   - Sam selects Allow once, and the file is written.
   - Ask the agent to write the file again. It prompts again, because nothing was remembered.
   - Deny that one, as you, with "not now". The agent is told "Denied by Emmz via Panopticon: not now".
5. **Away off with a prompt held:** ask for another question, then turn Away off while it's held. Within a few
   seconds, the terminal asks the question itself.
6. **The change page:** "Answers from the dashboard" lists your answer and Sam's.
7. **Clean up:** `rm away-test.txt`, and reinstall the plugin from GitHub afterwards if you want to.

- **On PASS:** tick 8.1 and 8.2, write the section-done note with Emmz's confirmation, and make the §8 commit. Then
  do the whole-change review (CLAUDE.md, "Finishing a change"), and offer `/code-review`.
- **On FAIL:** fix what failed. If it's the matcher, try `^(?!AskUserQuestion$).*`, or move the exclusion into the
  CLI. Rerun the gates and `make e2e`, then ask again.

## Next

- Waiting on Emmz: the 8.2 recipe above.
- **Deferred:** `PresenceStore.WaitAsync` times out on wall-clock time, not the `TimeProvider`.
- **Follow-up change:** a PWA, so notifications work from any page.
- **Still open with Emmz:** deleting `change/server-invites`, and the CS2012 CLAUDE.md note.
