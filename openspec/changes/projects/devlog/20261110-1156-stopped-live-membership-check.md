# Stopped: §11 needs Emmz's two-browser check

<!-- Section: 11 · Tasks: 11.1–11.3 · Base: 1e13f4c -->

**What's built (uncommitted, on purpose; 11.1 and 11.2 ticked, 11.3 not):**
- **11.1, the server:**
  - **Events:** `ChangeEvent.MembershipChanged(memberId, projectId, isMember)` and `ChangeEvent.MemberRemoved(memberId)`.
  - **Publishing:** after the commit, from invite, role change, removal or leaving, creating a Project, and removal
    from the server.
  - **The stream** (`DashboardEndpoints.StreamAsync`) applies the member's own events in queue order, then sends
    `projects-changed` `{ projectId, member }`. `MemberRemoved` ends their stream. The keep-alive re-check also sends
    `projects-changed` for any difference it finds (the backstop).
  - **Tests:** 5 new in `ScopedStreamTests`, including one endpoint test with a 10-minute keep-alive.
- **11.2, the web:**
  - **The dashboard:** `live.ts` handles `projects-changed`. Leaving calls `App.leaveProject`, which drops the cards and
    clears a `project=` filter on them. Joining refetches, and if the refetch fails it reconnects.
  - **The change page:** a matching leave refetches, and a 404 shows "Change not found" and closes the stream.
  - **Tests:** view tests in `cards.test.ts`, `change.test.ts` and `live.test.ts`.
- **Emmz's answers (2026-11-10):** removal from the server ends the streams at once. Creating a Project notifies. Role
  changes notify. The keep-alive backstop sends events. All are recorded in design D3.
- **A known gap, raised by a side note too:** an **archived** change's page has no live stream, so removal doesn't
  clear it until a reload. It's recorded in D3 as "Not live". **A question for Emmz:** is a reload good enough there?

**Gates:** `GATES_EXIT:0`, `E2E_EXIT:0`.

## Recipe (two browsers)

1. **Setup:** the same as `20261110-1120-stopped-human-verification.md` ("Setup", with the server rebuilt from this
   tree by `dotnet run`). Browser A is `dev@localhost.test` (Emmz, owner of PAN). Browser B is `sam@localhost.test`
   (Sam). A invites Sam to PAN, and an active PAN change has reported (Emmz's agent in a PAN-linked repo).
2. **Removed while watching:** B on the dashboard with PAN cards showing. A removes Sam from PAN on the Project page.
   → B's PAN cards (and the PAN filter choice) go at once, with no reload. A new PAN report doesn't appear on B.
3. **Removed while filtering:** as 2, but with B filtered to PAN (`/?project=…`). → The address goes back to `/`.
4. **Removed while on a change page:** B opens an **active** PAN change's page. A removes Sam. → B shows "Change not
   found".
5. **Invited while watching:** B on the dashboard. A invites Sam to PAN. → PAN's active cards appear at once, and the
   next PAN report updates live.
6. Optional: removing Sam from the server while B watches → B goes to sign-in within a second or two.

## Next

- **On PASS:** tick 11.3, write the section-11-done note, and commit
  `feat(projects): membership changes take effect live (§11)`. Then the whole-change review, then offer `/code-review`,
  then `/opsx:archive`.
- **On FAIL:** fix it within §11, then rerun the gates and the recipe.
- **Pending:** Emmz's answer on archived change pages. If she wants them covered, add a task to §11 (the page opens the
  stream only for `projects-changed`).
