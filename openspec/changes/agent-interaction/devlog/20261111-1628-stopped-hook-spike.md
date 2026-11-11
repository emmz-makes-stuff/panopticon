# Stopped: §1 hook spike needs Emmz to drive a real Claude Code

<!-- Section: 1 · Tasks: 1.1–1.2 · Base: 5dc5916 -->

Set up a throwaway spike, outside the repo and never committed, at
`/private/tmp/claude-501/-Users-rendle-github-emmz-panopticon/0c487fd8-7f00-40bf-bc92-40939a26ebef/scratchpad/hook-spike/`.
Its `.claude/settings.local.json` wires these hooks:

- `hooks/log.sh` appends every `PreToolUse`, `PostToolUse`, `PermissionRequest`, `Stop`, `UserPromptSubmit` and
  `SessionEnd` event to `events.jsonl`, with a UTC timestamp.
- `hooks/answer.sh` (sync, `timeout: 30000`) answers an `AskUserQuestion` only when a question contains `[auto]`. It
  returns `permissionDecision: "allow"` with `updatedInput` = the original input plus `answers` (the first option, or
  for multiple choice the first two joined by `", "`), and `additionalContext`. It logs what it returned.
- `hooks/permission.sh` allows `spike-allow` commands, and denies `spike-deny` ones with a message, through
  `decision.behavior`.
- `hooks/sleep.sh` (sync, `timeout: 30000`) holds for 900s on a Bash command containing `spike-sleep`, logging when
  it starts and finishes.

All four fall through (exit 0, no output) for anything else, so the terminal behaves normally. A dry run of
`answer.sh` and `permission.sh` with sample stdin produced the expected JSON.

**Waiting on:** Emmz to run the recipe (in the turn that wrote this note) and give back `claude --version`, what she
saw in the terminal at each step, and `events.jsonl`. **The questions:**

- Do the event fields match design D7's Context?
- Does the injected `AskUserQuestion` answer reach the agent?
- Do allow and deny work, with nothing remembered?
- Does a sync hook survive 15 minutes?

## Next

- On her results: read `events.jsonl` with `jq`. Record in a decision note:
  - the exact JSON that worked;
  - the multi-select join (`", "` or `","`);
  - whether `additionalContext` reached the agent;
  - whether the deny message showed;
  - that the sleep hook finished (a start with no finish means a kill);
  - the Claude Code version.
- Amend design D7 if anything differs, and fix the shapes for §2 and §5.
- Then tick 1.1–1.2, run `make gates`, commit §1 (planning docs only: design amendments and devlog), and go on to
  §2, task 2.1.
- If the 15-minute hold was killed, the 8-hour hold in D6 doesn't work. That's a design stop: ask Emmz before
  changing the release rules in the spec.
