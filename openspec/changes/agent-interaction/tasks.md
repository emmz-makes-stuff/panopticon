# Tasks

## 1. Spike: Claude Code hook shapes (human verification)

- [x] 1.1 Write a throwaway logging hook (`$TMPDIR` script plus a scratch `.claude/settings.local.json` in a scratch repo, never committed) that appends each event's stdin to a log file for `PreToolUse`, `PostToolUse`, `PermissionRequest`, `Stop`, `UserPromptSubmit` and `SessionEnd`. Give Emmz a copy-pasteable recipe to drive it in a real Claude Code: a Bash call with a description, an edit, an `AskUserQuestion` (single choice, multiple choice and Other), a permission prompt allowed and one denied, a turn end, and `/exit`. Verify: the log holds one entry per event, with `session_id`, `cwd`, `tool_input.description` and the `AskUserQuestion` input.
- [x] 1.2 With the same recipe, check D7's outputs: a sync `PreToolUse` hook on `AskUserQuestion` that returns `allow` with `updatedInput.answers` (and `additionalContext`); a `PermissionRequest` hook that returns allow, then deny with a message; and a sync hook with `timeout: 30000` that sleeps for 15 minutes without being killed. Also record the Claude Code version. Verify: the session carries on with the injected answer, the tool runs or is denied with the message, and nothing else prompts in the terminal. Record the exact JSON that worked, any difference from D7, and the version, in a devlog note, and amend D7 if needed.

## 2. Contracts and the data model

- [x] 2.1 Add the hook-endpoint contracts to `Panopticon.Contracts`, all source-generated in `ContractsJsonContext` (D3): `SessionEventRequest` (session state enum, activity), `PromptRequest` (question or permission payload), `PromptCreated`, `PromptWaitResult` (pending, answered or released, with the answer), and `AwayState`, plus the limits from the "Prompt limits" requirement as constants. Verify with round-trip serialisation tests, including an unknown state value refused.
- [x] 2.2 Add `Member.AwaySince` and the `RemoteAnswer` table in one migration with a down migration (D8). Verify with migration tests: it applies and rolls back, and deleting a change deletes its `remote_answers` rows.

## 3. Server: presence

- [x] 3.1 Add `PresenceStore` (D4): sessions keyed by member and session id, tied to the active change matched by scope, repo and name as reports are, with state, activity and times, and the expiry sweep at the stale threshold. Verify with unit tests on a fake clock: two sessions on one change, an event for a change with no card (stored nowhere visible), a `SessionEnd`, and expiry after 4 hours.
- [x] 3.2 Add `POST /api/agent/sessions/events` with the `Reporter` policy, the member from the token, and the activity length checked (D3). Publish `SessionUpdated` and `SessionEnded`, and add `Sessions` and `LastActiveAt` to `ChangeCard` reads, with the order still by last report (D4). Verify the "Session identity", "Session states", "Presence is not history" and "Who sees a session" scenarios with endpoint and `StreamAsync` tests, including Sam's outsider sweep and a metrics read unchanged by 400 events.

## 4. Server: away mode and prompts

- [x] 4.1 Add `Viewer.OwnedProjectIds` and `CanAnswer`, and carry the role on `MembershipChanged`, so open streams follow a promotion or demotion (D5). Verify with `Viewer` unit tests and a `StreamAsync` test where Bob is demoted while a prompt is held and his next event is redacted.
- [x] 4.2 Add the away endpoints (`/api/agent/away` for the `Reporter` policy, `/api/me/away` for `Member`) and the `away-changed` event, sent only to that member's streams (D8). Verify the "Away mode" scenarios with endpoint tests, including the flag surviving a new server instance on the same database.
- [x] 4.3 Add `POST /api/agent/prompts`, `PUT /api/agent/prompts/{id}/preview` and the `wait` long-poll (D3, D6): the server decides `held` from the away flag in the one call, a permission prompt carries no input until it's held, prompt limits are checked, and a prompt is released on away off, after 8 hours, or with no `wait` for 90s, with 404 for an unknown prompt. A held prompt keeps its session from the presence sweep, and the stale threshold counts from its release (D4). Verify with endpoint tests on a fake clock: held versus not held, a preview refused (409) for a prompt that isn't held, each release path, a `wait` that returns as soon as an answer lands, and the "Away overnight" scenario (a session holding a prompt for 5 hours is still on its card).
- [x] 4.4 Add `POST /api/prompts/{id}/answer` (D5, D6, D8): the database re-check of who may answer, a compare-and-set so the first answer wins, the `RemoteAnswer` row written in the same transaction, the session set back to working (a denied tool gets no `PostToolUse`, D6), and `answer-recorded` published. Make prompts full or redacted per viewer in card reads and the stream. Verify the "Who may answer", "What others see of a prompt", "Answers are attributed" and "Permission prompts" (server side) scenarios with endpoint tests, including two concurrent answers giving one 200 and one 409, and Sam's browser responses never containing the command text.
- [x] 4.5 Add the change page's answer list to its API, with the question texts and labels only for members who could have answered, and live via `answer-recorded`. Verify the `change-documents` "Answers given from the dashboard" scenarios with endpoint tests.

## 5. CLI: `panopticon hook`, `panopticon away` and the link file

- [x] 5.1 Make the bridge write `worktrees.json` after each accepted report and clear the entry on a matching archived report, without ever failing the report (D9). Verify the `cli-bridge` "Reported change recorded" scenarios with tests, including two worktrees, a refused report and an unwritable config directory.
- [x] 5.2 Add `ActivitySummary` (D2) and the change resolution (link file, then the `change/<name>` branch, then nothing) (D9). Verify the "Activity text" and "A session's change" scenarios with unit tests, including a Bash command never appearing in the request and a 300-character description cut to 120.
- [x] 5.3 Add `panopticon hook` for the async events (D1): dispatch on `hook_event_name`, an `AgentApiClient` with a 5s budget, exit 0 with no stdout on every failure, and a log file for errors. Verify the "Session events from hooks" and "Hooks never break the session" scenarios with tests that drive the published binary, as the CLI end-to-end tests do: a down server, no login, garbage stdin and an unknown event.
- [x] 5.4 Add the sync path of `panopticon hook` for `AskUserQuestion` and `PermissionRequest` (D6, D7): ignore a `PermissionRequest` whose tool is `AskUserQuestion` (D1), post the prompt (a permission one with its tool name only), exit at once when it isn't held, otherwise upload a permission prompt's preview and loop on `wait` until answered (writing D7's output, as amended in §1) or released (no output). Verify the "A question is not a permission prompt" scenario,Verify the "A question while not away", "A prompt while away", "A prompt while away", "Permission input stays local unless held", "Releasing a prompt", "Question forms" (the answer format) and "Prompt limits" scenarios with tests against a test server: a not-away permission prompt whose request bodies never contain the command, a release when away goes off, a 404, and losing the server for 60s.
- [x] 5.5 Add `panopticon away [on|off]`, and document `hook` and `away` in `docs/cli.md`. Verify with CLI tests: on, off, the state printed, and the not-logged-in error.
- [x] 5.6 Resolve a session's change from the `change/<name>` branch first, then the bridge's record while its change is still active, then the active change with the newest `tasks.md` (D9, amended in §6 by Emmz). Verify the amended "A session's change" scenarios with unit tests, including a branch switch before any report, a stale record and an archived change never counting.

## 6. Web: sessions, prompts and the away switch

- [x] 6.1 Add session rows to cards, with the activity or wait text and the elapsed time on the quiet clock, `LastActiveAt` driving the quiet state, and `session-updated` and `session-ended` handled in `live.ts` (D10). Verify "Sessions on a card", "Sessions don't reorder cards" and the modified "Quiet cards" and "Quiet state moves on by itself" scenarios with vitest tests.
- [x] 6.2 Add `promptForm.ts` and the "Needs you" and "Needs <member>" markers: single choice, multiple choice, yes/no, previews through the safe renderer, Other, Allow and Deny with a message, and inline errors for 403, 404 and 409. Verify "Needs you", the "Question forms" rendering, "Safe rendering" and the "Permission prompts" form scenarios with vitest tests, including the script-in-a-preview case.
- [x] 6.3 Add the away switch to the header (`aria-pressed`, a text label, `away-changed` live), the tab-title count, and opt-in browser notifications (D10). Add the answer list to the change page, live via `answer-recorded`. Verify "Away switch in the header", "Needs-you count in the tab title", "Browser notifications" and the change page list with vitest tests, including that permission is never asked for until the member opts in.
- [x] 6.4 Run the `frontend-design` pass on session rows, the prompt form and the away switch, then stop for Emmz's sign-off on the look and on live updates in a real browser. Verify: her answer, recorded in a devlog note.

## 7. Plugin (in `../panopticon-skills`)

- [ ] 7.1 Add `hooks/panopticon-hook.sh` (exits 0 when `panopticon` is missing or fails) and D1's `hooks.json` entries beside the existing `tasks-changed.sh` hook. Bump the plugin's version, and commit in that repo. Verify: `jq` parses `hooks.json`; and a test of the wrapper, using a fake `panopticon` on `PATH` and one missing from it, sees exit 0 and empty stdout.
- [ ] 7.2 Update the plugin's README and skills: away mode, `panopticon away`, what a held prompt looks like in the terminal, and the minimum Claude Code version from §1. Add the away-mode step to `/panopticon:setup`'s checklist. Verify: the README and skills name every new hook and command, and `/panopticon:setup`'s steps still run in order.

## 8. End to end

- [ ] 8.1 Extend the container e2e flow (`make e2e`): post session events and a held prompt through the published CLI with stdin fixtures, answer it through the dashboard API as the owner, and check the CLI's hook output and the `RemoteAnswer` row. Verify: `E2E_EXIT:0`.
- [ ] 8.2 Stop for human verification with a copy-pasteable recipe. Install the updated plugin and CLI in a real Claude Code session. Watch the activity on the card while gates run. Turn away mode on, answer a multiple-choice question and a permission prompt from the dashboard (once as Emmz, once as a Project owner), then turn away mode off with a prompt held, and check the terminal takes over. Verify: Emmz's confirmation, recorded in a devlog note.

## Workflow follow-up

- Whole-change review, offer `/code-review`, then `/opsx:archive`.
- Release the server, then the CLI, then the plugin (design Migration Plan).
- A follow-up change (Emmz, 2026-11-11): make the dashboard a PWA, so held-prompt notifications reach her from any
  page, or with the tab closed. Today they fire only while the cards page (`/`) is open.
