# Handoff: §6.1 built and green; 6.2's server half done; nothing committed

<!-- Section: 6 · Tasks: 6.1 built (unticked), 6.2 started · Base: 4aa8b0b -->

Context passed 40% just after 6.2 began. The whole of §6 so far is **uncommitted on purpose**, because the section
commits once, when it's done. Don't revert it.

**6.1, session rows: built and tested.**

- `web/src/sessionRows.ts`:
  - `sessionText` and `elapsed` (no "ago").
  - `updateSessions` patches the `ul.card-sessions` rows in place, keyed by `data-session-id`. A row is rebuilt only
    when its `data-signature` changes (name, machine, since, text, prompt, promptDetail).
  - `updateSessionTimes` and `lastActiveAt`.
- **Emmz's answers (2026-11-11):**
  - Rows read "Emmz on mbp: Run make gates, just now", with commas as `web/DESIGN.md` principle 5 asks, not the
    spec's middots.
  - A teammate sees "waiting for Emmz"; only the session's own member sees "waiting for you".
  - The scenarios in `specs/live-dashboard` ("Running gates", "Waiting since lunch") and the `remote-answers` "Not
    away" line are amended to commas (uncommitted).
- Wait texts: "waiting on a question", and "waiting on permission for <tool>" (or "waiting on permission" without
  one).
- `card.ts`:
  - `renderCard(card, now, quiet, viewerId?)` appends the rows after `.card-agents`.
  - `applyQuiet` works the state out from `lastActiveAt`. "last seen" uses the last activity; otherwise the time
    shown is the last report's. It also ticks the session times.
- `app.ts`:
  - `AppDeps.viewerId` comes from `views/cards.ts`, as `env.member()?.id`.
  - New `updateSession` and `endSession`. Both patch in place and update `lastActiveAt` locally.
  - A same-report card whose only difference is its sessions (in `render` and `upsert`) is patched in place, not
    rebuilt.
  - The order is still by `lastReportedAt`.
- `live.ts`: `SESSION_UPDATED` and `SESSION_ENDED`, with `parseSessionUpdate` (which checks the state) and
  `parseSessionEnd`.
- `types.ts`: `SessionView`, `SessionPrompt`, `PromptDetail`, `PromptQuestion` and `PromptOption`, plus optional
  `sessions` and `lastActiveAt` on `ChangeCard`.
- `styles.css`: a first pass at `.card-sessions`. 6.4 sets the final look.
- `web/src/sessionRows.test.ts` has 18 tests. Web gates: `WEB_TEST_EXIT:0` (481 tests), `WEB_LINT_EXIT:0`,
  `WEB_FORMAT_EXIT:0` and `WEB_BUILD_EXIT:0`. `make validate` printed `VALIDATE_EXIT:0`.

**6.2, started: the server half of safe previews.**

- **The finding:** the web app has no Markdown renderer. Documents are rendered on the server by `DocumentRenderer`
  (Markdig with `DisableHtml` and a filter that drops unsafe link targets).
- **So:** `Presence/SessionView.cs` gains `PromptQuestionView` and `PromptOptionView(Label, Description,
  PreviewHtml)`.
  - `PromptQuestionView.For` renders each option's preview with `DocumentRenderer.RenderMarkdown`.
  - `PromptDetail.Questions` is now `IReadOnlyList<PromptQuestionView>`, built in `PresenceStore.Ask`.
- A permission prompt's `Preview` stays raw text, a command. The web must show it as text, in a `<pre>` through
  `textContent`, never as HTML.
- `tests/Panopticon.Server.Tests/Presence/PromptQuestionViewTests.cs` has 3 tests: a script in a preview,
  `javascript:` links and images, and the fields carrying over. They passed, with the prompt endpoint and redaction
  tests: 18 in all.
- `web/src/types.ts` `PromptOption` now has `previewHtml` in place of `preview`.

**6.2 still to build: the plan.**

1. **`promptForm.ts`:**
   - One fieldset per question: radios, or checkboxes for multiSelect, each with its label and description. Insert
     `previewHtml` as is.
   - An Other text input. Multiple choice joins the chosen labels with ", ", and the Other text goes in as one more
     item.
   - A single-select question with exactly Yes and No shows two `aria-pressed` toggle buttons, plus Other.
   - Every question must be answered, or an inline error says so.
   - A permission prompt: the tool's name, the preview in a `<pre>`, Allow only once the preview is there, and Deny
     with an optional message of up to 500 characters.
   - It posts `/api/prompts/{id}/answer` with `{answers}` or `{allow, message?}`, through the app's `Api` (so
     `AppDeps.api`, passed on to `updateSessions`).
   - Errors show inline, in text: 403 (only the session's member and the Project's owners can answer), 404 (the
     prompt has gone) and 409 (already answered, or back in the terminal).
2. **Markers, in the row:**
   - With `promptDetail`, "Needs you". If held, the form; if not, the question texts and "answer in the terminal",
     with no form.
   - With `prompt` but no detail, "Needs <member>: question" or "Needs <member>: permission for <tool>".
   - Waiting on a prompt with no `prompt` at all (over the limits): "Needs you" for the session's own member, else
     "Needs <member>".
   - `waiting-on-user` never needs anyone.
   - Mark the card with a class (for example `card--needs-you`), for 6.3's tab-title count.
3. **Known limit:** a new report on the card rebuilds it, which would lose a half-typed answer. That's rare: the
   asking session is paused. Note it, or move the form across when the prompt id is unchanged.
4. **Tests (vitest):** "Needs you" (the Owner's view, Not away, A teammate's view); the Question forms rendering
   (multiple choice "Hooks, Web", Other, Yes or no); a script in a preview not running; the permission form (Deny with
   a message, Allow only after the preview); and the inline 403, 404 and 409 errors.

**Uncommitted files:**

- Modified:
  - the specs `live-dashboard` and `remote-answers`;
  - the server's `PresenceStore.cs` and `SessionView.cs`;
  - the web's `app.ts`, `card.ts`, `live.ts`, `styles.css`, `types.ts` and `views/cards.ts`.
- New: `tests/Panopticon.Server.Tests/Presence/PromptQuestionViewTests.cs`, `web/src/sessionRows.ts` and
  `web/src/sessionRows.test.ts`.

No background tasks are running. The branch has no upstream and nothing has been pushed.

## Next

- **Finish 6.2** from the plan above, then 6.3: the away switch in the header, the tab-title count, notifications and
  the change page's answer list.
- Then 6.4's `frontend-design` pass, which stops for Emmz's sign-off.
- Run `make gates` before the §6 commit; the full .NET suite hasn't run since the `PromptDetail` change.
- **Deferred:** `PresenceStore.WaitAsync` times out on wall-clock time, not the `TimeProvider`.
- **Still open with Emmz:** deleting `change/server-invites`, and the CS2012 CLAUDE.md note.
