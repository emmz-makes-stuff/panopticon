# Proposal

## Why

The dashboard shows where a change stands, but not what its agent is doing right now or whether it's stuck waiting
for a person. `report_progress` arrives a few times per section, so a card can sit unchanged for half an hour while
the agent runs gates. Nothing on it says the agent has been waiting on a question since lunch. And when Emmz is away
from her desk, the only way to unblock an agent is to go back to the terminal.

Claude Code's hooks make all three possible without the agent doing anything: they see every tool call, every
permission prompt and every question. They can also answer `AskUserQuestion` and permission prompts themselves.

## What Changes

- **Live activity per session.** The Claude Code plugin's hooks report each session's current activity, e.g. the Bash
  tool's own `description` ("Running make gates"), or "Editing tasks.md". It shows on the session's change card and
  updates as tools run. It is presence only: kept in server memory, never in the report history or the metrics.
- **Waiting indicators.** A session that has asked a question, hit a permission prompt or ended its turn shows as
  waiting, in text, with how long it has waited. The dashboard tab's title counts the sessions that need you, and a
  browser notification can say so.
- **Away mode.** One switch per member, on the dashboard or with `panopticon away on|off`. While it's on, every
  Claude Code session of that member sends its `AskUserQuestion` questions and permission prompts to the dashboard
  and waits there for the answer. Turning it off sends a waiting prompt back to the terminal. While it's off, the
  dashboard shows the question but you answer in the terminal.
- **Answering from the dashboard.** The card renders the question (single choice, multiple choice, yes/no, with
  previews and an "Other" free-text answer, up to four questions at once) or the permission request (allow or deny).
  The session's owner and the owners of its Project may answer. Every web answer names who gave it, to the agent and
  on the change page.
- **CLI.** New `panopticon hook` (run by the plugin's hooks, reading the hook's JSON from stdin) and `panopticon away`.
  The bridge records which change it last reported for each working tree, so a hook knows which card a session
  belongs to.
- **Plugin** (`panopticon-skills` repo). New hook entries for `PreToolUse`, `PostToolUse`, `PermissionRequest`,
  `Stop`, `UserPromptSubmit` and `SessionEnd`, all running `panopticon hook`.
- **Claude Code only.** This is a deliberate exception to "any MCP-capable agent" (`docs/DISCOVERY.md`). An agent
  that is waiting can't report through MCP that it's waiting, so these features use hooks. Other agents keep
  everything they have today.

## Capabilities

### New Capabilities

- `agent-presence`: Claude Code sessions on a change, each with its live activity and waiting state, reported by the
  plugin's hooks through `panopticon hook`, held in memory with expiry, and visible to whoever can see the change.
- `remote-answers`: Away mode, and answering a session's questions and permission prompts from the dashboard. Covers
  who may answer, what each viewer may see of a prompt, releasing a prompt back to the terminal, and the attributed
  record of each answer.

### Modified Capabilities

- `live-dashboard`: Cards show each session's activity and waiting state. A prompt can be answered on its card, the
  header has the away switch, and the tab title and browser notifications report sessions that need you.
- `cli-bridge`: After each report, the bridge records the reported change for its working tree, for `panopticon hook`
  to read.
- `change-documents`: A change's page lists the answers given from the dashboard to its sessions.

## Impact

- **Server:** a new in-memory presence and prompt store, REST endpoints for the CLI (API token, the `Reporter`
  policy) and for the dashboard (session, the `Member` policy), new live-stream events filtered per viewer, a
  `Member` away flag, and a `RemoteAnswer` table, with an EF migration.
- **Contracts:** new source-generated request and response records for the hook endpoints, shared with the AOT CLI.
- **CLI:** `panopticon hook`, `panopticon away`, and the bridge's change-link file.
- **Web:** session rows on cards, the prompt forms, the away switch, the tab title badge, browser notifications, and
  the change page's answer list.
- **`../panopticon-skills`:** `hooks/hooks.json` and the plugin's skills, and a version bump. That repo's sections
  are committed there.
- **No new dependencies.** It needs a Claude Code with the `PermissionRequest` hook and async hooks, and the plugin
  says which version.
