# Tasks

## 1. Server: the data model

- [x] 1.1 Add `Project` and `ProjectMember` (with the role check), and `changes.project_id` and `owner_member_id` (with the not-both check), and rebuild both change identity indexes with the scope columns and `NULLS NOT DISTINCT` (D1). Verify that the migration applies, with a test that two rows differing only by scope can coexist and two with the same scope can't.
- [x] 1.2 Backfill `owner_member_id` on existing changes from each change's earliest reporting member, and write the down migration (D1, Migration Plan). Verify with `MigrationTests`: an older change with a member's report becomes theirs, a bootstrap one stays both-null, and the migration rolls back.

## 2. Server: Projects and their members

- [x] 2.1 Add `GET`/`POST /api/projects` and `GET`/`PATCH /api/projects/{id}` (D5): code validation and upper-casing, a case-insensitive unique code (409), name rules, owner-only rename, and 404 for non-members. Verify "Creating a Project", "Project codes", "Project names" and "Projects list" with endpoint tests.
- [x] 2.2 Add the member endpoints: invite by email with a role (creating or reactivating the `Member`), change a role, remove, and leave (D5). Lock the Project row so the last-owner rule holds. Verify "Inviting to a Project" and "Changing roles and removing" with endpoint tests, including two owners demoting each other at once leaving one owner.

## 3. Server: reporting into Projects

- [x] 3.1 Add the optional `project` to `ReportProgressArguments` and `ReportInput`. `ReportRecorder` resolves the scope inside the report's transaction, and refuses an unknown, malformed or not-a-member Project with an error naming the id and `list_projects` (D4). Verify "Reporting into a Project", "Report from a CLI without Projects" and the new "Change identity" scenarios ("Two scopes, two changes", "Two people, no Project") with recorder and MCP endpoint tests.
- [x] 3.2 Move personal work into a Project on its first linked report: the D4 lookup order, moving the reporter's own active personal change, or archiving it when the Project already has that change. Verify "Linking moves personal work" and "Linking when the Project already has the change" with recorder tests: one change afterwards, with all its events, time to archive counted from the first report, a teammate's personal change untouched, an archived change not moved, and the live events sent.
- [x] 3.3 Add the server's `list_projects` MCP tool, with its contracts in `Panopticon.Contracts` (D6). Verify with MCP endpoint tests for a member's Projects and roles (and nothing else) and for a bootstrap reporter getting every Project.

## 4. Server: visibility everywhere

- [x] 4.1 Add `Viewer` and `VisibleTo` (D2), and require a `Viewer` in `ChangeCards`, `ChangePages` (page, documents, history and history choices) and `MetricsReader`, so hidden changes answer 404 and choices don't leak. Verify "Who sees a change", "Hidden changes don't exist" and "Development bootstrap reports" with endpoint tests, plus the outsider sweep test over every change-reading endpoint.
- [x] 4.2 Scope the live stream (D3): events carry their change's scope, each subscription filters by its viewer, and the keep-alive re-check reloads the viewer's Projects. Verify "Live updates are scoped" with `StreamAsync` tests: another Project's report isn't sent, and a viewer removed mid-stream stops getting updates after one keep-alive.
- [x] 4.3 Add the Project to `ChangeCard`, history entries and `ChangePage` (`project: { id, code, name } | null`, `personal`). Add the `project` filter (a Project id or `personal`) to `HistoryFilter` and the history and metrics endpoints, and `projects` to `HistoryChoices` (D9). Verify "Filter by Project" and "Project filter choices" for the history and metrics with endpoint tests, and that a Project the viewer isn't in matches nothing.

## 5. Server: members rescoped

- [x] 5.0 Retired personal copies (D4): add `retired_to_project_id` to changes (nullable, a foreign key to projects, with a migration), set when linking retires a personal change. The team metrics leave retired copies out. History entries get a nullable `archiveFolder` and a `linkedTo` Project. Verify the "A copy retired by linking" (history) and "Retired copies don't count" (metrics) scenarios with recorder and endpoint tests.
- [x] 5.1 Rescope the member endpoints (D8):
  - `GET /api/members` lists people who share a Project, and yourself;
  - `POST /api/members` is removed;
  - `DELETE /api/members/{id}` is for initial members only, is refused for a last owner (naming the Projects), and also deletes the member's Project memberships;
  - `GET /api/session` gains `canRemoveMembers`.

  Verify the modified "Member list" and "Removing a member" scenarios with endpoint tests, and move the old invite tests to the Project invite.

## 6. CLI: the link file and `list_projects`

- [x] 6.1 Add `ProjectLink`, which reads `.panopticon.json` at the repo root with source-generated JSON (D7), and send its id with each report. A malformed file is a tool error naming the file, with nothing sent. Verify "Project link file" and "Malformed link file" with bridge tests (a linked repo, no file, invalid JSON, a non-UUID), and that the AOT publish is still warning-free (`make aot`).
- [x] 6.2 Add the bridge's `list_projects`, forwarded through a general `ServerForwarder.CallAsync` (D6). Verify "Listing Projects through the bridge" and the modified "Stdio MCP server" scenario with bridge tests against a test server (a result, no login, a refusal). Extend the CLI end-to-end test so a linked repo's report lands in its Project.
- [x] 6.3 Document `.panopticon.json` and `list_projects` in `docs/cli.md`: what the file holds, committing it, and asking the agent to link the repo. Verify the wording against the spec.

## 7. Web: Projects pages and members

- [x] 7.1 Add `/projects`: the list with roles, and a create form with code validation and the taken-code message. Add `/projects/{id}`: the name (editable by owners), code and id, the copyable `.panopticon.json`, members with roles, and owners' invite, role-change and remove, plus Leave and the last-owner message. Add the Projects header link. Verify with view tests against the fake server, covering an owner and a viewer.
- [x] 7.2 Update the Members page: no invite form (a line pointing to Projects), and Remove shown only when `canRemoveMembers`. Verify with view tests, and update the shell's and the other pages' header link expectations.

## 8. Web: the Project in the views

- [x] 8.1 Show the Project's code, or "Personal", on cards, the change page header and history rows. Verify with view tests ("Card shows its Project", "History row shows its Project").
- [x] 8.2 Add the `project` filter to the dashboard (filtering in the browser, with choices from the cards), the history and the metrics (choices from the server), offered by code and name. Verify "Filter by Project", "Project filter choices" and "One Project's metrics" with view tests, and that live updates respect the Project filter.
- [x] 8.3 Show a retired copy's history row as "Linked to <code>" in place of its archive folder, with `archiveFolder` typed `string | null` on history entries. Verify "A copy retired by linking" with a view test.

## 9. Design pass and docs

- [x] 9.1 Run the `frontend-design` skill for the Projects list, the Project page (members, roles, the link block) and the code label on cards and rows (desktop and phone, light and dark), within `web/DESIGN.md`. Add a "Projects" section to it and apply the styles. Verify `make web-build` and `make web-lint`, and record the choices in a devlog note.
- [x] 9.2 Update the README: Projects and roles, linking a repo with `.panopticon.json`, who sees what (personal work included), that people now join through a Project, and who can remove someone from the server. Mark the Projects items as built in `docs/DISCOVERY.md`. Verify the wording against the specs.

## 10. Integration and human verification

- [x] 10.1 Run `make gates` and `make e2e`. Extend the e2e flow so it creates a Project, links the test change folder with `.panopticon.json`, and checks that the card shows the code. Verify `GATES_EXIT:0` and `E2E_EXIT:0`.
- [x] 10.2 Write the human verification recipe in a devlog note, then stop for Emmz to run it:
  - two people in two browsers, in different Projects, each seeing only their own Project's cards, history and metrics, live;
  - a real agent (Claude Code through `panopticon mcp`) calling `list_projects`, writing `.panopticon.json`, and its next report landing in the Project;
  - inviting, changing a role, the last-owner refusal, and removing someone while they watch;
  - the Projects pages and the code label in light and dark, on desktop and phone.

## 11. Review fixes: membership changes take effect live

- [x] 11.1 On the server: after a Project membership change commits (invite, role change, removal or leaving, and server-wide removal), update the scope of that member's open subscriptions at once, and send them a `projects-changed` event naming the Project and whether they're now in it. Verify with `StreamAsync` and endpoint tests: removed mid-stream, no later update of that Project arrives and the event is sent; invited mid-stream, the event arrives and the Project's updates follow.
- [x] 11.2 On the web: on `projects-changed`, the dashboard drops a left Project's cards and filter choice, or refetches the cards for a joined one. An open change page of a left Project shows "not found". Verify the new "Membership changes take effect live" scenarios with view tests.
- [x] 11.3 Run `make gates` and `make e2e`, then stop for Emmz to check in two browsers: Sam removed while watching the dashboard and a change page, and Sam invited while watching.

## Workflow follow-up

- Run the whole-change review and offer `/code-review` before archiving.
