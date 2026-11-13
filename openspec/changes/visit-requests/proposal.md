# Proposal

## Why

Agents often need their member to look at something only a person can: review and approve a PR, approve a pipeline
run, or check that a page looks right. Away mode lets a member answer questions and permission prompts from the
dashboard, but an agent has no way to say "please open this URL and tell me when you're done". It has to bury the link in
an `AskUserQuestion`, where the link is plain text and the request reads like any other question. Claude Code has no hook
for "the agent needs a URL visited", so the agent has to ask for it explicitly.

## What Changes

- The bridge (`panopticon mcp`) gets a new MCP tool, `request_visit`. It takes the `url` (`http` or `https`), a `reason`,
  and an optional `action` (what to do there, e.g. "approve if the checks are green"). Its description tells agents
  when to use it.
- While the member is away, the plugin's sync `PreToolUse` hook holds the call as a new kind of prompt, a **visit
  request**. It uses the same hold, release and answer flow as questions and permission prompts. The dashboard card
  shows the reason and the action, the full URL as a link that opens in a new tab, **Done** and **Can't** buttons, and an
  optional note. The agent gets the outcome, the note and "answered by <member> via Panopticon".
- While the member isn't away, or when the request can't be held, the tool returns at once. It tells the agent to ask in
  the terminal instead, with the URL in the question.
- Visit requests follow the existing rules:
  - who may answer;
  - what other viewers see ("Needs Emmz: visit a page");
  - release (away off, 8 hours, server lost for over a minute);
  - the tab-title count and browser notifications.
- The answer record gains the visit kind, with its URL and outcome. The note is not recorded.
- The plugin (in `panopticon-skills`) gets a sync hook entry for `request_visit`, and the async catch-all stops
  matching it. The `report-progress` skill gets a line on when to use the tool.

## Capabilities

### New Capabilities

None: visit requests are a new kind of prompt in `remote-answers`.

### Modified Capabilities

- `remote-answers`: adds visit requests. They are held while away and answered Done or Can't with a note. They have
  their own limits and URL rules, and are attributed and recorded.
- `cli-bridge`: the stdio MCP server offers `request_visit`. It validates its input and returns the dashboard's answer,
  or tells the agent to ask in the terminal.
- `agent-presence`: a session can be waiting on a visit request.
- `live-dashboard`: "Needs you" covers visit requests, with their form for those who may answer.

## Impact

- **Contracts** (`Panopticon.Contracts/Prompts.cs`):
  - `PromptKind.Visit`;
  - a `Visit` payload on `PromptRequest`;
  - the visit outcome on `PromptAnswer`;
  - limits in `PromptLimits`.
- **CLI**:
  - `BridgeTools` gets `request_visit`;
  - `HookCommand` recognises the tool's `PreToolUse`;
  - `PromptHookOutput` injects the answer into the tool's input.
- **Server**:
  - `PresenceStore`, `AgentPromptEndpoints`, `PromptAnswerEndpoints` and the redacted live stream handle the new kind;
  - `RemoteAnswer` gets a URL column (one migration).
- **Web**:
  - `promptForm.ts` gets a visit form;
  - `card.ts` and `answerList.ts` get the visit wording;
  - `types.ts` gets the new kind.
- **Plugin** (`../panopticon-skills`): `hooks.json`, the `report-progress` skill, the README, and a version bump.
- No new dependencies.
