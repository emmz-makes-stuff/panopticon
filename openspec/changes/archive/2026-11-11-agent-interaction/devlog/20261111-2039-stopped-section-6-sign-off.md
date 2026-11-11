# Stopped: §6 is built and green, and waits on Emmz's sign-off (6.4)

<!-- Section: 6 · Tasks: 6.1–6.4 built, all unticked · Base: 4aa8b0b -->

All of §6 is **uncommitted on purpose**. The section commits once, after Emmz signs off on 6.4. Don't revert it.

## Built

**6.1, session rows:** see `20261111-2019-handoff-section-6.md`. Since then:

- A row is now `li > .card-session-line` (the text), plus an optional `.card-session-needs` block.
- `renderCard` and `updateSessions` take a `SessionViewer` (`{ id, api }`) in place of `viewerId`. `app.ts` builds
  one from `deps.viewerId` and its `Api`.

**6.2, the prompt form and the markers:**

- `web/src/promptForm.ts`:
  - One fieldset per question: radios, or checkboxes for multiSelect. The labels and descriptions are text, and
    `previewHtml` goes in as the server rendered it.
  - An Other text box. Typing in it clears a single-choice selection, and choosing an option clears it.
    `joinAnswer` joins the labels with ", " and adds the Other text as one more item.
  - A single-choice question with exactly Yes and No shows as two `aria-pressed` buttons, plus Other (`isYesNo`).
  - An unanswered question gives the inline error `Answer "<header>" first.` and posts nothing.
  - A permission prompt: "Permission for Bash", the preview in a `<pre>` through `textContent`, and Allow once
    (disabled until the preview arrives). Deny takes an optional message of up to 500 characters.
  - It posts through the app's `Api`. A 403, 404 or 409 gives fixed text and disables the form. Any other failure
    gives the server's `detail` and leaves the form usable.
- `sessionRows.ts` `needs()`:
  - "Needs you" when the viewer has the detail, or is the session's member.
  - Otherwise "Needs <member>: question" or "Needs <member>: permission for <tool>".
  - "Needs <member>" when the prompt was over the limits, and nothing for `waiting-on-user`.
  - The card gets `card--needs-you`. A held prompt shows its form (when there's an `Api`); one that isn't held
    shows the questions and "answer in the terminal".
- **The known limit is fixed, not just noted.** `keepPromptForms` moves an open form with the same prompt id onto a
  card rebuilt by a new report (`render`, `upsert` and `replaceQuietly`), so a half-typed answer survives.
- `web/src/promptForm.test.ts` has 25 tests.

**6.3, the away switch, the tab title, notifications and the answer list:**

- `web/src/away.ts`:
  - A `.away-switch` toggle in the header. Its accessible name stays "Away", `aria-pressed` gives the state, and
    the visible text reads "Away off" or "Away on".
  - It loads with `GET /api/me/away` and changes with `PUT {away}`, showing the server's answer. A failure shows
    inline in `.account-error`.
- `away-changed` reaches the switch through `Env.awayChanged`, from the cards view (`connectLive`'s `onAway`) and
  from the change page's stream.
- `api.ts`: `Api.send` now takes `PUT`.
- Tab title:
  - `heldPrompts(cards)` lists the sessions whose `promptDetail.held` is true.
  - `AppDeps.onHeld` is called from `layout()` whenever that list changes. The cards view sets
    `document.title = titleWith(n)`, which gives "(2) PANOPTICON", and resets it on teardown.
- `web/src/notifications.ts`, `createNotifier`:
  - The opt-in is the `panopticon.notifications` key in localStorage, wrapped in try/catch, with an in-memory
    fallback.
  - The browser's permission is asked only from `enable()`.
  - `held()` notes the prompts on its first call, then raises one notification (`tag`: the prompt id) for each new
    one.
  - Clicking a notification goes to `/` if needed, then scrolls to the card and focuses its link.
- The Notifications pill in the header is hidden where the browser has no notifications.
- `web/src/answerList.ts`:
  - The change page's "Answers from the dashboard" section, from `ChangePage.answers`.
  - A row reads "Bob answered a question for Emmz's session, 5m ago" or "Emmz denied Bash for Emmz's session",
    with question/answer pairs only when the server sent them.
  - `answer-recorded` events for this change are merged in once each (`withAnswer`).
- New `types.ts` types: `AwayState`, `AnswerEntry` and `AnsweredQuestion`. New `live.ts` parsers:
  `parseAwayState` and `parseAnswerEntry`.
- `testShell.ts` serves `GET /api/me/away`, and takes a `notifications` host (unsupported by default).
- Sign out now has a `.sign-out` class, and `shell.test.ts` selects it by that.
- `web/src/header.test.ts` has 14 tests, and `views/change.test.ts` has 3 more.

**6.4, the `frontend-design` pass.** It's in `styles.css`, and recorded in `web/DESIGN.md` as the new
"Sessions and prompts" section:

- "Needs you" is a `--paper` well with a 3px `--spark` rule, and its card takes a spark hairline border.
- A teammate's "Needs Emmz" is one muted line.
- The form uses the pages' vocabulary:
  - Send answer and Allow once are solid ink, and the other buttons are outlined.
  - Inputs use `accent-color: ink`, never teal.
  - The command is the one monospace block.
- Away and Notifications are hairline pills that turn solid ink when on. Away was spark in the first pass.
- Answers are rows between hairlines.

**I haven't seen it rendered.** No screenshot was taken, so the look is unchecked until Emmz runs the recipe.

## Gates

- After 6.3: `make gates` printed `GATES_EXIT:0`. That's `BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0` (the full .NET
  suite, after the `PromptDetail` change), `FORMAT_EXIT:0`, `WEB_BUILD_EXIT:0`, `WEB_TEST_EXIT:0`,
  `WEB_FORMAT_EXIT:0`, `WEB_LINT_EXIT:0` and `VALIDATE_EXIT:0`.
- After the design pass and a fix for an unhandled `requestPermission` rejection: `WEB_TEST_EXIT:0`,
  `WEB_LINT_EXIT:0`, `WEB_FORMAT_EXIT:0` and `WEB_BUILD_EXIT:0`. These changes are web-only.

## Questions for Emmz

1. **Sign-off on the look**: session rows, the "Needs you" well and the form, the header pills and the answer list.
   Also on the live updates, seen in a real browser.
2. **Notifications only fire while the cards page (`/`) is open.** That's my call, not the spec's.
   - On History, Metrics or a change page, a newly held prompt raises nothing.
   - Prompts already held when the page loads don't notify either.
   - Is that OK? The fix would be one shell-level stream, or a check of `/api/changes` from the other pages: a scope
     change.
3. **The tab title reads "(2) PANOPTICON".** The spec's scenario says "(2) Panopticon", but `web/DESIGN.md` makes
   the name PANOPTICON everywhere. Can I amend the scenario?

## Recipe (copy-paste)

```sh
cd ~/github/emmz/panopticon
docker compose up -d --wait
npm --prefix web ci && npm --prefix web run build
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

Then:

1. Open http://localhost:7480 and sign in as `dev@localhost.test` (the link is in the server log). Check the
   header: "Away off" and "Notifications off" pills.
2. In another terminal:
   - `make aot && artifacts/cli/panopticon login --server http://localhost:7480`
   - Get a card for this change: report from a Claude Code session in this repo against the local server (README,
     "Connect an agent").
3. A working row:
   `echo '{"hook_event_name":"PreToolUse","session_id":"look-1","cwd":"'"$PWD"'","tool_name":"Bash","tool_input":{"command":"make gates"}}' | artifacts/cli/panopticon hook`
   Without a reload, the card should show "Emmz on <machine>: …, just now".
4. Select **Notifications** (the browser asks only now), then **Away**, which turns solid ink and reads "Away on".
5. A held question. It blocks, so leave it running:
   `echo '{"hook_event_name":"PreToolUse","session_id":"look-1","cwd":"'"$PWD"'","tool_name":"AskUserQuestion","tool_input":{"questions":[{"question":"Which store?","header":"Store","multiSelect":false,"options":[{"label":"Memory","description":"fast, gone on restart"},{"label":"Postgres","preview":"**durable**, <script>alert(1)</script>"}]}]}}' | artifacts/cli/panopticon hook`
   - The tab title should read "(1) PANOPTICON", and one notification should appear.
   - The card should show the "Needs you" well and the form. The preview shows the script as text.
   - Pick an option and select Send answer: the hook prints its answer and exits, and the well goes away.
6. A held permission prompt:
   `echo '{"hook_event_name":"PermissionRequest","session_id":"look-1","cwd":"'"$PWD"'","tool_name":"Bash","tool_input":{"command":"make e2e","description":"Run e2e"}}' | artifacts/cli/panopticon hook`
   Deny it with "not now". Then open the change page: "Answers from the dashboard" lists both answers.
7. Select **Away** again to turn it off.

- **On PASS:** record her answers to questions 2 and 3, tick 6.1–6.4 and write the section-done note. Make the one
  §6 commit, then move on to §7.1 (in `../panopticon-skills`).
- **On FAIL:** fix what she names, rerun the web gates, and ask again.

## Next

- Waiting on Emmz: questions 1–3 above.
- **Deferred:** `PresenceStore.WaitAsync` times out on wall-clock time, not the `TimeProvider`.
- **Still open with Emmz:** deleting `change/server-invites`, and the CS2012 CLAUDE.md note.
