# Design

## Context

- **Reports** (`report_progress`) go from the bridge to the server's online MCP endpoint (`MapMcp`, the `Reporter`
  policy, API token) through `ServerForwarder`, which makes a short-lived MCP client for each call, with a 30s timeout.
  Every accepted report is a `ReportEvent` with documents in S3, and the card is computed from those events.
- **Agents** are `Agent` rows (member, client name, machine name). `ChangeAgent` ties one to a change, with
  `LastReportedAt`. A card's `CardAgent` list comes from those rows.
- **The live stream** (`ChangeBroadcaster`) fans `ChangeEvent`s out to 64-deep queues, one per subscription, on a
  single server instance (D9 of the first change). `StreamAsync` filters by `Viewer.CanSee(scope)`, and per-viewer
  content is already done once: `Updated.CardFor(memberId)` for personal tags (tags D6).
- **`Viewer`** holds a member's Project ids, with no roles. `MembershipChanged` carries `IsMember` only.
- **The CLI** is Native AOT. Contracts are source-generated in `Panopticon.Contracts` (`ContractsJsonContext`). The
  config lives in `CliConfigFile` (0600 file, 0700 directory, atomic writes).
- **The plugin** (`../panopticon-skills/plugins/panopticon`) has one `PostToolUse` hook today. It tells the agent to
  call `report_progress` after a `tasks.md` edit.
- **Claude Code hooks**, from the hooks docs (verified in §1 before anything is built on them):
  - Every event carries `session_id`, `cwd` and `hook_event_name`. `PreToolUse` and `PostToolUse` also carry
    `tool_name` and `tool_input`, and Bash's `tool_input` has `description`.
  - A command hook can run with `"async": true`. Its default timeout is 600s.
  - A `PreToolUse` hook on `AskUserQuestion` can answer it by returning `permissionDecision: "allow"` with
    `updatedInput` (the original `questions` plus `answers`, mapping question text to the chosen label, labels joined
    by commas). It can also return `additionalContext`.
  - A `PermissionRequest` hook returns `decision.behavior` `allow` or `deny`, with `message` on a deny.

## Goals / Non-Goals

**Goals:**
- No new effort or tokens for the agent: everything comes from hooks.
- A hook can never wedge a session. Every failure falls back to what Claude Code would have done with no hook.
- Prompt content reaches only the members who may answer it, through every read path and the live stream.
- Presence never touches the event log, S3, history or metrics.

**Non-Goals:**
- Agents other than Claude Code, and new MCP tools.
- Claude Code channels: a research preview needing a development flag at every launch.
- Answering MCP elicitation dialogs, plan-mode exits, or anything but `AskUserQuestion` and permission prompts.
- Running presence across more than one server instance.
- Sending the agent a new instruction or message from the dashboard (beyond answering what it asked).
- Sessions on the history page, and session counts in metrics.

## Decisions

### D1. Hooks call `panopticon hook`; the plugin owns the wiring

There is one CLI entry, `panopticon hook`. It reads the event from stdin, dispatches on `hook_event_name`, and is
run from one wrapper script in the plugin. The wrapper exits 0 when `panopticon` isn't on `PATH`. The hook entries:

| Entry | Matcher | Mode | Timeout | Does |
| --- | --- | --- | --- | --- |
| `PreToolUse` | everything but `AskUserQuestion` | async | 10s | working + activity |
| `PreToolUse` | `AskUserQuestion` | sync | 30000s | waiting on a question; holds it when away (D6) |
| `PermissionRequest` | all but `AskUserQuestion` | sync | 30000s | waiting on a permission; holds it when away (D6) |
| `PostToolUse` | all | async | 10s | working (clears a wait) |
| `Stop` | | async | 10s | waiting on the user |
| `UserPromptSubmit` | | async | 10s | working |
| `SessionEnd` | | async | 10s | session removed |

The existing `tasks-changed.sh` `PostToolUse` hook stays as it is.

**Found in §1 (Claude Code 2.1.322):**

- Claude Code raises a `PermissionRequest` for every `AskUserQuestion` that a `PreToolUse` hook doesn't answer, at
  the same moment as the `PreToolUse`. The permission entry excludes it, and `panopticon hook` also ignores a
  `PermissionRequest` whose `tool_name` is `AskUserQuestion`, in case the matcher misses. Otherwise every question
  would be posted twice.
- A question that the `PreToolUse` hook answers raises no `PermissionRequest`.
- Bash commands that the sandbox auto-approves raise no `PermissionRequest` at all, so they never reach the
  dashboard.

**Alternative:** MCP tools that the agent calls (`set_activity`, `ask`). Rejected: an agent that's waiting can't
call a tool to say so, and asking it to report activity costs tokens and gets forgotten. **Alternative:** one
script per event. Rejected: the event logic would be split between shell and C#, with no tests for the shell side.

### D2. Activity text is made in the CLI, and tool input never leaves the machine

`ActivitySummary` maps `tool_name` and `tool_input` to one line, cut to 120 characters:

- Bash: its `description`, else "Running a command".
- `Edit`, `MultiEdit`, `Write`, `NotebookEdit`: "Editing <file name>". `Read`: "Reading <file name>".
- `Grep`, `Glob`: "Searching the code". `WebFetch`, `WebSearch`: "Searching the web".
- `Agent` or `Task`: "Running agent: <description>". `Skill`: "Using skill <name>".
- `mcp__<server>__<tool>`: "Calling <server> <tool>". Anything else: "Using <tool>".

Only the summary is sent for activity. A question prompt carries its `AskUserQuestion` questions: the agent's own
text, shown on the card even when the member isn't away. A permission prompt's input leaves the machine only after
the server has held the prompt, and then only as a preview cut to 2 KB (D3, D6). A Bash command can hold a token,
and while the member is at the keyboard nobody on the web needs to see it.

### D3. The CLI talks REST, not MCP

The hook endpoints are plain JSON under `/api/agent/`, with the `Reporter` policy and the same bearer token. They
use source-generated contracts in `Panopticon.Contracts`, and an `AgentApiClient` in the CLI shares
`ServerForwarder`'s error mapping. They aren't tools for an agent. Putting them on `MapMcp` would add them to the
tool list of any MCP client that connects to the online endpoint, and each call would pay for an MCP handshake.

- `POST /api/agent/sessions/events`: `{repo, change, project?, machine, sessionId, state, activity?}` gives 204, or
  204 with nothing changed when no active change matches (spec: Session identity).
- `POST /api/agent/prompts`: the session fields plus `{kind: "question", questions}` or `{kind: "permission",
  tool}` gives `{promptId, held}`. A permission prompt never carries its input here. **The server decides `held`**
  from the member's away flag in this single call. The hook never asks "am I away?" first, so a toggle between two
  calls can't misroute a prompt.
- `PUT /api/agent/prompts/{id}/preview`: a held permission prompt's preview (at most 2 KB) gives 204, and 409 for a
  prompt that isn't held or isn't a permission. Until the preview arrives, the dashboard shows the prompt with Deny
  only.
- `GET /api/agent/prompts/{id}/wait`: a 25s long-poll that gives `{state: pending|answered|released, answer?}`, or
  404 when the server no longer knows the prompt (say, after a restart). An answer is kept until a wait reads it, or
  `WaitGrace` passes, even when the session's next event lands first (review fix 10.2).
- `DELETE /api/agent/prompts/{id}` (review fix 10.2): the hook gave up before waiting, so a pending prompt goes back
  to the terminal and can't be answered from the dashboard. It gives 204, and 404 for a prompt the server doesn't
  know. The hook calls it as a best effort.
- Session events carry an optional `occurredAt`, the time the hook's process started (review fix 10.1). The server
  ignores an event older than the last one it applied for the session, and, for `LateEventWindow` (1 minute), one no
  newer than a stamped `SessionEnd`. Events from an older CLI carry no time, and are applied as they arrive. A prompt
  carries the same stamp: it's never ignored (the terminal is waiting on it), but a late event from before it is
  (review fix 11.2).
- The live stream sends `resync`, with no data, when its queue has dropped events (review fix 10.3). The dashboard
  and the change page then load their state again, the away switch included, as they do on a reconnect.
- `GET` / `PUT /api/agent/away` for `panopticon away`, and `GET` / `PUT /api/me/away` for the dashboard (the
  `Member` policy).
- `POST /api/prompts/{id}/answer` (`Member`): `{answers}` or `{allow, message?}` gives 200, 403 when the caller may
  not answer, 404 when the prompt is gone, and 409 when it's already answered or released.

### D4. Presence lives in a singleton in-memory store

`PresenceStore` keys sessions by `(memberId, sessionId)`. Each holds its change id and scope, the machine, the
state, the activity and its start time, the last event time, and any prompt. A background sweep drops sessions
whose last event is older than the stale threshold (`ActivityOptions`), but never one holding a prompt: a held prompt
can wait 8 hours (D6), longer than the default 4-hour threshold, and the paused session sends no events meanwhile. A
released prompt's session expires as usual, counted from the release (decided with Emmz in §3). Each change publishes
on the broadcaster:

- `SessionUpdated(changeId, scope, session)` and `SessionEnded(changeId, scope, sessionId)`, sent as
  `session-updated` and `session-ended`.
- Card reads (`ChangeCards.ListAsync`, the change page) add `Sessions` from the store. So a reconnect's refresh
  includes them, and nothing in the database changes.
- `ChangeCard` gets `LastActiveAt = max(LastReportedAt, newest session event)` for the quiet state (spec: Quiet
  cards). The order stays by `LastReportedAt`.

The store is in-process, which matches the broadcaster's single-instance assumption. **Alternative:** a
`ChangeAgentSession` table. Rejected: that means a database write for every tool call, for state nobody reads later.

### D5. Per-viewer prompts: full or redacted, chosen per subscriber

A session's prompt is held twice: a full view, and a redacted one (kind, plus the member it needs). Like
`Updated.CardFor` (tags D6), `StreamAsync` and the card reads give each viewer the full view only when
`Viewer.CanAnswer(session)` says yes:

- The session's member is the viewer, or the change's Project is in `Viewer.OwnedProjectIds`.

`Viewer` gains `OwnedProjectIds`, loaded with the roles. `MembershipChanged` gains the new role, so a promoted or
demoted owner's open stream follows at once. The answer endpoint checks again against the database at answer time,
so a stale stream can never authorise an answer.

### D6. Holding a prompt, and releasing it

The sync hook posts the prompt. If `held` is false, it exits 0 with no output, and the terminal asks as usual. The
card shows the prompt (full or redacted, D5) until a `PostToolUse` or `UserPromptSubmit` clears it. For a permission
prompt it shows only the tool's name. If `held` is true, a permission hook first uploads its preview. Then the hook
loops on `wait`:

- `answered`: write the hook output (D7) and exit 0.
- `released`, a 404, an 8-hour total, or no successful `wait` for 60s: exit 0 with no output, and the terminal asks.

A denied tool gets no `PostToolUse` (seen in §1). So when the server records a web answer, it clears the session's
wait itself, setting it to working. A deny in the terminal is cleared by the session's next `PreToolUse`, `Stop` or
`UserPromptSubmit`.

On the server, a held prompt is released when the member turns away off (all their held prompts at once), after 8
hours, or when no `wait` call has come for 90s (the hook is gone). Answering is a compare-and-set on the prompt's
state under a lock, so exactly one answer wins (spec: Two answers at once). The hook's 30000s timeout is longer than
the 8 hours, so the CLI always gives up before Claude Code kills it.

### D7. The hook output that answers

- **Question:** `hookSpecificOutput: {hookEventName: "PreToolUse", permissionDecision: "allow", updatedInput:
  {questions: <as asked>, answers: {<question text>: <labels or free text>}}, additionalContext: "Answered by <name>
  via Panopticon."}`.
- **Permission allowed:** `hookSpecificOutput: {hookEventName: "PermissionRequest", decision: {behavior: "allow"}}`,
  with no `updatedPermissions`, so nothing is remembered.
- **Permission denied:** `decision: {behavior: "deny", message: "Denied by <name> via Panopticon: <message>"}`.

**Confirmed in §1** against Claude Code 2.1.322, exactly as above:

- The agent received the injected answers (`{"[auto] Pick one":"Red","[auto] Pick some":"Hooks, Web"}`) and the
  `additionalContext`, and the terminal asked nothing. The terminal's own multiple-choice answers use the same
  `", "` join.
- A permission allow ran the tool once and saved no permission rule.
- A deny stopped the tool, and the agent got the message as the tool's error.
- A sync hook with `timeout: 30000` held for 900s without being killed. The full 8 hours wasn't tested.

### D8. Away flag and the answer record

- `Member.AwaySince` (nullable timestamp) is the away flag. Turning it off releases held prompts (D6) and publishes
  `AwayChanged(memberId)`, which only that member's streams get (`away-changed`).
- `RemoteAnswer` rows hold: id, change id, the session's member id, the answering member id, the time, the kind, the
  tool name (for permissions), the outcome (`allow`/`deny`), and `QuestionsJson` (question texts and the chosen
  labels, never previews). The answer endpoint writes the row in the same transaction as its database re-check.
  The change page API returns each entry with `QuestionsJson` only for viewers who could have answered (D5's rule),
  and publishes `answer-recorded` for live change pages.
- One migration adds both, with a down migration.

### D9. The session's change: a link file next to the CLI config

After each accepted report, the bridge writes `{<worktree root>: {change, at}}` into `worktrees.json`, in the CLI
config's directory, with the same atomic 0600 write as the config file. An archived report removes the entry when it
still names that change. The hook resolves the git top-level directory of `cwd`, then takes the change from the first
of: a `change/<name>` branch; its entry, while that change is still active in `openspec/changes/`; the active change
whose `tasks.md` changed last. With none, it sends nothing.

**Amended during §6 (Emmz, 2026-11-11):** the entry came first at first, so after a switch from a branch that had
reported `tags` to `change/projects`, activity landed on the `tags` card until the agent reported `projects`. The
branch now wins, a stale entry is skipped, and the newest `tasks.md` is the last fallback. The repo identity and `.panopticon.json`
come from the same code the bridge uses (`ProgressReporter.ParseRemote`, `ProjectLink`).

Keying by worktree root means two worktrees never collide. Two sessions in one worktree, on two changes, would
share an entry. That is accepted (see Risks).

### D10. Dashboard UI

- **Session rows** on the card, under its agents: member · machine · the activity or wait text · the elapsed time,
  ticking with the quiet-state clock.
- **The prompt form** is a new `promptForm.ts` component: radio buttons or checkboxes per question, previews rendered
  by the existing safe Markdown renderer, an Other text box, and Allow / Deny with a message box. It reports errors
  (403, 409, 404) inline in text.
- **The away switch** sits in the shell header: a real toggle button with `aria-pressed` and a text label.
- **The tab title** is computed from the live state on every change.
- **Notifications** are opt-in, from a header menu item. Whether they're on is kept in `localStorage`, wrapped in
  try/catch. One `Notification` is raised per newly held prompt id, and clicking it focuses the card.
- The look and feel needs a `frontend-design` pass and human sign-off (§6).

## Risks / Trade-offs

- [The hook JSON shapes differ from the docs] → §1 checks them on a real Claude Code before §5 depends on them, and
  the CLI's output builder is one small, unit-tested class.
- [An 8-hour sync hook is beyond what Claude Code allows] → §1 also checks that a 30000s timeout holds. If there's a
  lower cap, the hold time drops to fit it.
- [A blocked terminal confuses Emmz when she comes back and forgets she's away] → the hook prints nothing, but the
  card says "held for the dashboard". `panopticon away off` releases within one poll. The plugin's skill explains it.
- [Secrets in commands] → a command's preview leaves the machine only for a held prompt (D2, D3). While away, a
  preview with a token in it does reach the members who may answer: they need to see what they're allowing. Redacting
  secrets from previews is out of scope.
- [A Project owner allowing a command on someone else's machine] → owners only, attributed to the agent and on the
  change page, never remembered as a rule, and the preview is shown before allowing.
- [Many hook processes per minute] → async hooks don't block tools, the native CLI starts fast, and each event is
  one small POST. The server keeps only the latest state per session.
- [Two sessions in one worktree on different changes] → they show under the change the bridge reported last. That's
  acceptable for a rare setup, and `change/<name>` branches avoid it.
- [A server restart drops held prompts] → `wait` gets a 404 and the terminal asks at once. Nothing is lost but time.
- [The hook ends a prompt the user is mid-answering on the web] → the answer endpoint returns 409 or 404, and the
  form says the prompt went back to the terminal.

## Migration Plan

- Server first: the migration adds a nullable column and a new table, so older CLIs and plugins carry on as before.
- Then the CLI release with `panopticon hook` and `away`, then the plugin release with the new hooks. If a plugin
  update arrives before its CLI, the hook command fails and the wrapper exits 0, so nothing breaks.
- Rollback: revert the plugin's `hooks.json` first. The server's endpoints and table can stay.

## Open Questions

- Whether a sync hook can hold for the full 8 hours. §1 showed 15 minutes. The CLI gives up at 8 hours whatever
  happens, so a lower cap in Claude Code would only send the prompt back to the terminal sooner. The plugin's README
  names 2.1.322, the version tested in §1, as the minimum.
