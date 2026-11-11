# Handoff: §4 built and tested, not yet reviewed, gated or committed

<!-- Section: 4 · Tasks: 4.1–4.5 built, unticked · Base: 917ea26 -->

All five §4 tasks are built, and each one's tests pass when its test class runs alone. Context passed 40% after 4.5,
so the section's self-review, `make gates`, ticks, section-done note and commit are left to the next session. The
whole section is **uncommitted on purpose**; don't revert it.

**What was built:**

- **4.1, who may answer:**
  - `Viewer` gained `OwnedProjectIds`, loaded from the roles. The owned ids are always a subset of `ProjectIds`.
  - `WithProject(projectId, string? role)` takes the role now; null means the member left. `CanAnswer(sessionMemberId,
    scope)` is true for the session's own member, or an owner of the change's Project.
  - `MembershipChanged` carries `Role` (`IsMember` is worked out from it), and every `ProjectEndpoints` publish site
    passes the role.
- **The prompt model** (`Presence/SessionView.cs`, split out of `PresenceStore.cs`):
  - `SessionView` gained two init properties. `Prompt` (`SessionPrompt`: kind and tool) goes to everyone who can see
    the session. `PromptDetail` (id, held, asked-at, questions, preview) goes only to viewers who may answer.
  - `SessionView.For(viewer, scope)` strips the detail for anyone else. On the web side, a present `promptDetail` is
    what means "Needs you".
- **Redaction everywhere a card goes out:**
  - `ChangeCard.SessionsFor(viewer, scope)` applies D5's rule to a card.
  - `LiveCard.For(Viewer, ChangeScope)` replaced `For(Guid)`. `Updated.CardFor(Viewer)` is what the stream sends.
  - The stream redacts `session-updated` and `answer-recorded` per viewer. Card list reads, the change page and the
    tag endpoint's returned card all redact too.
- **4.2, away mode** (`Presence/AwayEndpoints.cs`):
  - `AwayMode` (scoped) has `GetAsync` and `SetAsync`. Turning on twice keeps the first time. Turning off calls
    `PresenceStore.ReleaseHeld`. Every call publishes `AwayChanged`, which goes to that member's streams only as
    `away-changed`.
  - Endpoints: `/api/agent/away` (Reporter) and `/api/me/away` (Member). A bootstrap token gets 403.
- **4.3, prompts** (`PresenceStore` rewritten, `Presence/AgentPromptEndpoints.cs`):
  - **Store:** a session holds at most one prompt.
    - **Held:** only when the member is away and the change has a card. A prompt that isn't held is `Released` from
      the start, so a `wait` on it answers `released` at once.
    - **Events:** any session event clears a prompt that isn't pending. A pending one stays put, and only refreshes
      `LastEventAt`.
    - **Release:** `Sweep()` releases held prompts at the 8h limit or after 90s with no `wait`. It never expires a
      session whose prompt is pending (Emmz's §3 ruling).
    - **On release:** `Settle` sets `held` false and resets `LastEventAt`, so the stale threshold counts from the
      release.
  - **`PromptOptions`** (`Panopticon:Prompts`, validated at start-up) holds `HoldLimit` 8h, `WaitGrace` 90s and
    `WaitTimeout` 25s. Tests set these through `TagWorld.CreateAsync(..., settings:)`. The sweep now runs every 15s,
    for the 90s grace.
  - **Endpoints:** `POST /api/agent/prompts` gives 400 for bad session fields or `PromptLimits.Problem`, and 403 for
    a bootstrap token. **Race guard:** after holding a prompt, it re-reads `AwaySince`; if away went off meanwhile, it
    releases and returns `held:false`. `PUT …/preview` gives 204, or 400 over 2 KB, 404 for unknown or another
    member's prompt, and 409 when the prompt isn't held or isn't a permission. `GET …/wait` gives 200 or 404.
- **4.4, answers** (`Presence/PromptAnswerEndpoints.cs`, `POST /api/prompts/{id}/answer`, Member):
  1. `Find` the prompt: 404 if unknown, 409 if not answerable.
  2. Check the body against the kind (400). An answer must have exactly the question texts as keys, each 1–2000
     characters. A permission answer needs `allow`, and a message is at most 500 characters.
  3. In one transaction, re-check against the database (the session's member, or an owner of the change's Project;
     403 otherwise) and insert the `RemoteAnswer` row.
  4. `TryClaim`: a lost claim gives 409, and the disposed transaction rolls back.
  5. Commit; if the commit fails, `Unclaim`.
  6. `CompleteAnswer`: the session goes to working, its prompt is cleared, and the hook is woken.
  7. Publish `AnswerRecorded`, and return 200 with the `AnswerEntry`.

  Claimed prompts are skipped by both `ReleaseHeld` and `Sweep`. A deny's `Message` is passed on trimmed, and an
  allow carries none.
- **4.5, the answer list:** `ChangePage.Answers` (init) lists up to 200, newest first, each `AnswerEntry.For(viewer,
  ...)`. `QuestionsJson` holds `[{question, answer}]`, written with the web JSON options.

**Tests (each class passed alone):**

| Test class | Passed |
| --- | --- |
| `ViewerTests` | 6 |
| `PromptRedactionStreamTests` | 3 |
| `AwayEndpointTests` | 5 |
| `AgentPromptEndpointTests` | 12 |
| `PromptAnswerEndpointTests` | 9 |
| `ChangePageAnswersTests` | 1 |

The updated `PresenceStoreTests`, `AgentSessionEndpointTests`, `ScopedStreamTests` and `TagStreamTests` also pass.
The full `make gates` hasn't run on §4 yet.

**Gotchas:**

- `perl -0pi -e 's|…|…|'` breaks on C# that contains `||`, because the pattern delimiter clashes. It silently
  mangled `AgentSessionEndpoints.cs`, which was restored from git and redone with Edit. Use the Edit tool for C#.
- Records compare their lists by reference, and so do tuples holding lists: assert on each list on its own.
- `HttpStatusCode` sorts by its number, so OK (200) comes before Conflict (409).

**Untracked new files:** `src/Panopticon.Server/Presence/{AgentPromptEndpoints,AwayEndpoints,PromptAnswerEndpoints,SessionView}.cs`
and `tests/Panopticon.Server.Tests/Presence/{AgentPromptEndpoint,AwayEndpoint,ChangePageAnswers,PromptAnswerEndpoint,PromptRedactionStream}Tests.cs`,
`tests/Panopticon.Server.Tests/Projects/ViewerTests.cs`. Everything else is modified tracked files (`git status`).

No background tasks are running. The branch still has no upstream; don't push unless asked.

## Next

- **Finish §4 (base `917ea26`):**
  1. Self-review `git diff 917ea26` plus the untracked files, against the `remote-answers`, `agent-presence` ("A held
     prompt keeps its session") and `change-documents` requirements.
  2. `make gates`: unsandboxed, in the background, writing to the scratchpad. If `Cli.IntegrationTests` stalls past
     about 2 minutes, see the open-follow-ups memory.
  3. Tick 4.1–4.5, write the section-done note (copy the decisions above into it), and make one commit:
     `feat(agent-interaction): away mode, held prompts and answers from the dashboard (§4)`.
- **Then §5, task 5.1:** the bridge writes `worktrees.json` (D9). The hook's `AgentApiClient` must send
  `X-Panopticon-Request: 1`, or every non-GET gets 403.
- Still open: deleting `change/server-invites`, and the CS2012 CLAUDE.md note, are unanswered.
