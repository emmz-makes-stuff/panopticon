# Tasks

## 1. Spike: `updatedInput` on an MCP tool (human verification)

- [x] 1.1 In a scratch repo (never committed), write a throwaway sync `PreToolUse` hook for a scratch MCP tool. The hook returns `allow` with `updatedInput` that adds a `panopticon_answer` object and `additionalContext`. The tool is a tiny bridge build or a stub stdio server that echoes its arguments. Give Emmz a copy-pasteable recipe to drive it in a real Claude Code. Also check that the matchers in design D3 match `mcp__plugin_panopticon_panopticon__request_visit` and exclude it from the catch-alls, using node's `RegExp`. Verify: the tool receives `panopticon_answer`, the session sees the `additionalContext`, and no permission prompt appears. Record the exact JSON, the Claude Code version, and which D2 path holds (as designed, fallback parameter, or stop) in a devlog note, and amend D2 if needed.

## 2. Contracts and the answer record

- [x] 2.1 Add `PromptKind.Visit`, `VisitRequest`, `PromptRequest.Visit`, `PromptAnswer.Visited`, and the visit limits in `PromptLimits.Problem`: an absolute `http`/`https` URL of at most 2,048 characters with no user info, a reason of 1–500 characters, an action of at most 1,000, and no questions or tool. Add the visit fields to the dashboard's answer body (design D4). Verify with round-trip serialisation tests and a `Problem` test for each limit, including `file:` and `https://bob:secret@…` URLs refused.
- [x] 2.2 Add `RemoteAnswer.Url` and the `done`/`cant` outcomes in one migration with a down migration (D5). Verify with migration tests: it applies and rolls back.

## 3. Server: visit requests as prompts

- [x] 3.1 Make `PresenceStore`, `POST /api/agent/prompts` and `wait` handle the visit kind: held only while away, released on away off, after 8 hours or on server loss, and a session waiting on a visit request (D5, D6). Verify the `remote-answers` "Visit requests" scenarios and the `agent-presence` "A visit request" scenario with endpoint tests on a fake clock, including a visit request over the limits refused with 400.
- [x] 3.2 Accept visit answers in `POST /api/prompts/{id}/answer`: refuse a shape that doesn't match the kind, refuse a note over 1,000 characters, and write the `RemoteAnswer` row with the URL and outcome but not the note. Redact visit requests per viewer in card reads and the stream. Return the URL in the change page's answer list only to members who could have answered (D5). Verify the "Visit request form" (server side), "What others see of a visit request" and "Visit answers are attributed" scenarios with endpoint and `StreamAsync` tests, including that Sam's responses never contain the URL, reason or action.

## 4. CLI: `request_visit` and the hook

- [x] 4.1 Add `request_visit` to `BridgeTools`. Its description is the agent instructions of design D8. It checks the limits, returning a tool error that names the problem, and returns the injected `panopticon_answer` as text and structured content. With no answer, it returns "not reached: ask in the terminal, with the URL in the question" at once (D2). Update the `cli-bridge` tool list. Verify with unit tests for answered, not answered and each limit, plus the "Agent lists tools" scenario through the stdio server test.
- [x] 4.2 Make `HookCommand` recognise the visit tool by D3's pattern, post a `visit` prompt, wait as for a question, and write `PromptHookOutput` for a visit. That output is an `allow` with `updatedInput` (the agent's `panopticon_answer` stripped, the hook's injected) and `additionalContext`. It writes nothing when the request isn't held, is released, or is over the limits. Verify with `PromptHookCommandTests` and a `PromptHookServerTests` integration test against the published binary: an answered visit's exact output, not away, released, and an over-limit URL.
- [x] 4.3 Document `request_visit` in `docs/cli.md`: what it's for, what the member sees, and the "ask in the terminal" reply. Verify the page's tool list matches the "Agent lists tools" scenario.

## 5. Dashboard: the visit form

- [ ] 5.1 Add the visit kind to `types.ts`, and a visit form to `promptForm.ts` (design D7). It shows the reason and the action as text, the full URL as a `target="_blank" rel="noopener noreferrer"` link (only for `http:`/`https:` after `new URL()`), Done, Can't and a note with `maxlength=1000`. Verify with `promptForm.test.ts`: the link's attributes, a `javascript:` URL shown as text, and the answer body for Done and for Can't with a note.
- [ ] 5.2 Give `card.ts`, `answerList.ts`, the tab-title count and notifications the visit wording: "Needs you" with the form, "Needs Emmz: visit a page", "visited <url>, done", and "<change>: visit a page". Verify the `live-dashboard` "A visit request" scenario and the answer-list scenario with `card.test.ts`, `answerList.test.ts` and the title and notification tests.

## 6. Plugin: hooks and instructions (`../panopticon-skills`)

- [ ] 6.1 On a `change/visit-requests` branch in `panopticon-skills`, add the sync `PreToolUse` entry for `request_visit`, and exclude it from the async catch-all and from `PermissionRequest` (design D3). Bump the plugin to 0.3.0 in `plugin.json` and `marketplace.json`. Verify: `jq -e .` parses each file, `plugins/panopticon/tests/panopticon-hook.test.sh` passes, and a node check shows each matcher matches or skips the tool as D3 says.
- [ ] 6.2 Add the `request_visit` line to the `report-progress` skill, and a "Visit requests" section to the plugin README (D8). Commit in `panopticon-skills`. Verify the skill and README name the tool exactly as the bridge lists it.

## 7. End to end (human verification)

- [ ] 7.1 Give Emmz a copy-pasteable recipe for a real Claude Code with the local CLI and the plugin branch. While away, ask the agent to request a visit to a real PR, answer Done with a note on the dashboard, then repeat with Can't. Then turn away off with a request held, and finally make a request while not away. Verify: one card entry per request (not two); the link opens in a new tab; the agent gets the outcome, note and "answered by Emmz"; away off and not away give "ask in the terminal"; the change page records both answers without the notes. Then run `make e2e` and `make gates`.

## Workflow follow-up

- Merge the `panopticon-skills` branch once a CLI with `request_visit` is released (design D9).
- Archive the change after the reviews in CLAUDE.md's "Finishing a change".
