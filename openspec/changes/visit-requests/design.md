# Design

## Context

Away mode (archived change `agent-interaction`) holds two kinds of prompt: an `AskUserQuestion` question, and a
permission prompt. The plugin's sync hooks run `panopticon hook`. The hook posts the prompt to
`POST /api/agent/prompts`, and the server decides in that one call whether to hold it. When it's held, the hook
long-polls `wait` for up to 8 hours and writes D7's output. For a question, that output is a `PreToolUse` `allow` with
the answers injected through `updatedInput`.

The bridge (`panopticon mcp`) is a separate stdio process. It doesn't know the Claude Code session id, and its MCP tool
calls are subject to the client's tool-call timeout. The plugin's hooks and skills live in the separate
`panopticon-skills` repo (`../panopticon-skills`).

## Goals / Non-Goals

**Goals:**

- An agent can ask its member to visit a URL, and while she's away she can answer **Done** or **Can't**, with a note,
  from the dashboard.
- Reuse the prompt flow (hold, release, who may answer, redaction, attribution, title count, notifications) rather than
  build a second one.
- Degrade cleanly: an old plugin, an old CLI, or a member who isn't away all leave the agent with a clear "ask in the
  terminal".

**Non-Goals:**

- Detecting the need to visit a URL without the agent asking. No hook can see it.
- Checking that the member really visited the page or did the action.
- Showing the page itself (screenshots, embeds) on the dashboard.
- Visit requests from agents other than Claude Code.

## Decisions

### D1. The hold happens in the sync `PreToolUse` hook, not in the MCP tool

`request_visit` is an ordinary bridge tool, and the plugin adds a sync `PreToolUse` hook for it, like the one on
`AskUserQuestion`. The hook posts a `visit` prompt and waits. The tool runs only afterwards, and reads the answer that
the hook put into its input (D2).

- **Why:** the hook already has the session id and `cwd`, so it can place the request on the right card. It already
  has the 8-hour hold, the release paths and the server-loss rule. Its `timeout: 30000` covers 8 hours.
- **Alternative rejected:** holding inside the bridge's tool call. The bridge would need the session id, which Claude
  Code doesn't give MCP servers. It would also need a second copy of the wait loop, and it would run into the client's
  MCP tool timeout long before 8 hours.

### D2. The answer reaches the tool through `updatedInput`

When the request is answered, the hook writes a `PreToolUse` `allow` whose `updatedInput` is the agent's input plus
`panopticon_answer: { outcome: "done" | "cant", note, answeredBy }`. It also writes the
`additionalContext` "Answered by <member> via Panopticon.", as for questions. The tool returns that answer as text and
structured content. With no `panopticon_answer`, the tool returns "not reached: ask in the terminal, with the URL in
the question" at once.

- The hook strips any `panopticon_answer` the agent sent before injecting its own. When it has no answer, it writes
  nothing, so an agent that invents the field deceives only itself (see Risks).
- **Spike first (§1):** confirm on a real Claude Code that a `PreToolUse` `updatedInput` on an MCP tool reaches the
  server with the extra property, and that the C# MCP SDK binds it.
  - If the extra property is refused, the fallback is to declare an optional `panopticon_answer` parameter whose
    description says the hook sets it, never the agent.
  - If `updatedInput` doesn't work for MCP tools at all, stop and ask.

### D3. Telling a visit request apart

`HookCommand` treats a `PreToolUse` as a visit request when its `tool_name` matches `^mcp__.*panopticon.*__request_visit$`.
That covers both the plugin's server (`mcp__plugin_panopticon_panopticon__request_visit`) and a hand-added
`mcp__panopticon__request_visit`. The plugin's `hooks.json` uses the same pattern:

- a new sync `PreToolUse` entry with that matcher and `timeout: 30000`;
- the async `PreToolUse` catch-all becomes `^(?!AskUserQuestion$|mcp__.*panopticon.*__request_visit$)`, so a visit
  request isn't posted twice (the same trap as `AskUserQuestion`, agent-interaction devlog 21:50);
- the `PermissionRequest` matcher excludes it the same way. Then a permission rule on the tool never turns into a held
  permission prompt about a visit request.

The CLI also checks the visit limits (D4) before posting. A request over the limits writes nothing, and the tool itself
returns the error.

### D4. Contracts

In `Prompts.cs`:

- `PromptKind.Visit`.
- `PromptRequest.Visit`, a `VisitRequest(Url, Reason, Action?)`. `PromptLimits.Problem` checks the visit limits from
  the spec, and refuses `Questions` or `Tool` on a visit.
- `PromptAnswer.Visited` (`true` for Done, `false` for Can't), and `Message` reused for the note.

The dashboard's answer body gets the same two fields. The server refuses an answer whose shape doesn't match the
prompt's kind.

### D5. Server

- `PresenceStore`, the prompt endpoints and the per-viewer redaction handle the third kind. A redacted visit request
  carries only its kind, so the card reads "Needs Emmz: visit a page".
- `RemoteAnswer` gets a nullable `Url` column and the outcomes `done` and `cant`, in one migration with a down
  migration. The change page's answer list returns the URL only to members who could have answered, under D5's rule.
- Away off, 8 hours and the server-loss rule release visit requests exactly as they release other prompts. No new
  code path is needed.

### D6. Not away: post anyway, let the server decide

As for questions, the hook always posts. The server answers `held: false` when the member isn't away, and the hook
writes nothing, so the tool runs at once. The card shows "waiting on a visit request" for the moment it takes, then
`PostToolUse` sets working. This keeps the server as the one place that decides `held` (agent-interaction D3).

### D7. Dashboard

- `promptForm.ts` gets a visit form with:
  - the reason and the action, as plain text;
  - the URL in full as the link text, so the host is visible;
  - Done, Can't, and an optional note (`maxlength=1000`).
- The link is `target="_blank" rel="noopener noreferrer"`. The browser parses the URL with `new URL()` again and
  renders a link only for `http:`/`https:`. Anything else shows as text, though the server refuses it first.
- `card.ts`, `answerList.ts`, the tab-title count and notifications get the visit wording. Notifications say
  "<change>: visit a page".

### D8. Agent instructions

No hook can detect "I need a person to look at this", so the instructions go where the agent sees them:

- the tool's description, which is always in context when the tool is loaded;
- one line in the plugin's `report-progress` skill, which agents load while building a change: "When you need your
  member to look at or act on a web page, call `request_visit` and wait for its answer";
- the plugin README and `docs/cli.md`.

### D9. Releasing the plugin and the CLI

The plugin goes to 0.3.0 on a `change/visit-requests` branch in `panopticon-skills`, as in agent-interaction's
Migration Plan, and merges once a CLI with `request_visit` is released.

- A new plugin with an old CLI: the old hook sees a `PreToolUse` on an unknown tool, sends a working event, and
  writes nothing. The old bridge has no `request_visit`, so the agent never calls it.
- A new CLI with an old plugin: no sync hook, so the tool always returns "ask in the terminal". But the old async
  `PreToolUse` catch-all still matches `request_visit`, so the new CLI posts the visit from an async hook that Claude
  Code kills after 10 seconds. While the member is away, the card then shows a held visit for about 90 seconds, until
  the server's wait grace releases it, though the agent has already moved on. That is harmless but confusing, so the
  plugin README says to update the CLI and the plugin together. (Found in §4.)

## Risks / Trade-offs

- **The spike may show `updatedInput` doesn't reach MCP tools.** D2 has a fallback for a refused extra property; a
  total failure stops the change for a decision before any other section is built.
- **An agent can put `panopticon_answer` in its own call.** Then it only misleads itself: nothing reaches the server or
  the record. The tool's description doesn't mention the field, and the hook overwrites it whenever it answers.
- **URLs can carry secrets in the query string** (signed links, tokens). The member's own agent is sending them to the
  member's own server, and the URL is shown and recorded only for members who may answer. URLs with a user name or
  password are refused outright. Stripping queries would break most "approve this run" links.
- **Phishing-shaped links.** The link is shown in full, opens with no opener and no referrer, and comes only from the
  member's own sessions.
- **A visit request answered "Done" proves nothing.** It is the member's word, recorded with who said it, like any other
  answer.

## Open Questions

None.
