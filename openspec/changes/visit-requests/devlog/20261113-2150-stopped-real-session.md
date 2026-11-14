# Stopped: §7.1 needs a real Claude Code session

<!-- Section: 7 · Task: 7.1 pending · Base: aa2922b · Plugin repo: ../panopticon-skills on change/visit-requests -->

Pre-flight on resume: `DEPS_EXIT:0`, `VALIDATE_EXIT:0`. Tree clean. Nothing built in §7 yet: it's human verification
only, then `make e2e` and `make gates`.

## The recipe, for Emmz

**Setup:**

1. Start the local server:
   ```sh
   cd ~/github/emmz/panopticon
   docker compose up -d --wait
   npm --prefix web run build
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
   ```
2. In a second terminal, install this branch's CLI and log it in:
   ```sh
   cd ~/github/emmz/panopticon && make install && panopticon login --server http://localhost:7480
   ```
3. In Claude Code, use the local plugin (`../panopticon-skills` is on `change/visit-requests`):
   ```text
   /plugin marketplace remove panopticon-skills
   /plugin marketplace add ~/github/emmz/panopticon-skills
   /plugin install panopticon@panopticon-skills
   ```
   Restart `claude` in `~/github/emmz/panopticon`. `/panopticon:setup` should pass.

**Checks:**

1. **Away on**, then ask the agent: "Use request_visit to ask me to review
   https://github.com/emmz-makes-stuff/panopticon-skills/pull/1, action: check the hooks diff."
   - The terminal asks nothing. The card shows **one** entry, not two.
   - The link opens in a new tab.
   - Answer **Done** with a note. The agent reports Done, the note, and "answered by Emmz".
2. Repeat, and answer **Can't**.
3. **Away off with a request held:** ask for another visit, then turn Away off. Within a few seconds the agent is
   told to ask in the terminal, and does, with the URL.
4. **Not away:** ask for one more. It returns at once with "ask in the terminal".
5. **The change page** lists the Done and Can't answers, without the notes.
6. **The form** looks right in light and dark.

- **On PASS:** tick 7.1, run `make e2e` and `make gates`, write the section-done note with Emmz's confirmation, and
  commit §7. Then the whole-change review, and offer `/code-review`.
- **On FAIL:** fix what failed, rerun the gates and `make e2e`, then ask again.

## Next

- Waiting on Emmz: the recipe above.
- **Still open after §7:** push the plugin branch and open its PR only once Emmz says so. It merges after a CLI
  release (design D9).
