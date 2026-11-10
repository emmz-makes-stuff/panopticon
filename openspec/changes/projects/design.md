# Design

## Context

- **Today, nothing is scoped.**
  - **Reading:** every endpoint that reads changes (`ChangeCards`, `ChangePages`, `MetricsReader`, the history choices)
    queries `db.Changes` directly, and the SSE broadcaster fans every event out to every subscriber.
  - **Members:** any member can invite (`POST /api/members`) and remove anyone. `GET /api/members` lists everyone.
- **Change identity:** an active change is unique on `(repo_id, name_key)` among unarchived rows, and an archived one
  on `(repo_id, archive_folder)`. `ReportRecorder.FindOrAddChangeAsync` locks the row with `SELECT … FOR UPDATE`.
- **The bridge:** it builds each report locally from git and the change folder, then forwards it with an MCP client
  (`ServerForwarder`) to the server's `report_progress`. Its config is the machine-wide `config.json`. It knows
  nothing about the repo besides git.
- **Members:** `Member` has no roles. The initial members come from `Panopticon:InitialMembers` (`InitialMembers`)
  and are seeded at start-up, but the server doesn't keep a record of who they are.
- **Not live yet:** there's no deployment, so existing rows are Emmz's local data. The migration has to keep them
  working, not preserve their visibility exactly.

## Goals / Non-Goals

**Goals:**
- **One rule for who sees a change,** applied by one helper on every read path and on the live stream. Then a new
  endpoint can't forget it without a test noticing.
- **The scope is part of change identity,** so two scopes never share a row.
- **Project membership,** with owner, editor and viewer roles and the last-owner rule, safe under concurrent edits.
- **Linking a repo needs only a committed file and an MCP tool.**

**Non-Goals:**
- Tags of either kind, and `editor`'s tag powers. The role exists, and behaves as a viewer here.
- Deleting or archiving a Project, moving a change between scopes, and invitation emails.
- Per-Project settings (quiet thresholds and the like), and Postgres row-level security.

## Decisions

### D1. Data model

- **`projects`:**
  - **Columns:** `id uuid` (from `Guid.NewGuid()`, a v4), `name`, `code`, `code_key` (lower-case, unique), `created_at`
    and `created_by_member_id`.
  - **Checks:** the code is validated in code (`^[A-Za-z][A-Za-z0-9]{1,9}$`) and stored upper-case.
- **`project_members`:** `(project_id, member_id)` as the primary key, with `role` (`owner`, `editor` or `viewer`; a
  check constraint), `joined_at` and `invited_by_member_id`. It's indexed on `member_id`, for "my Projects".
- **`changes`:** gains `project_id uuid null` and `owner_member_id uuid null`, with a check that they aren't both set.
  - **Project scope:** `project_id` set.
  - **Personal scope:** `owner_member_id` set.
  - **Both null:** a bootstrap report with no Project, or an old row with no member. Every signed-in member sees these
    (spec change-visibility, Development bootstrap reports).
- **Identity indexes:** both existing unique indexes gain the two scope columns, with `NULLS NOT DISTINCT` (Postgres 15
  or later; we run 17):
  - `(repo_id, name_key, project_id, owner_member_id) WHERE archived_at IS NULL`;
  - `(repo_id, archive_folder, project_id, owner_member_id) WHERE archive_folder IS NOT NULL`.
- **The migration** sets `owner_member_id` on existing changes, from the member behind each change's earliest report event
  that has one (null when no report had a member: a bootstrap or old row). So Emmz's local data becomes her personal work.
  - **Two members on one change:** it goes to the first reporter. That's acceptable before going live.
- **Alternative rejected:** a single `scope_key` text column (`p:<id>` or `m:<id>`). It's simpler to index, but it
  loses the foreign keys, and every query would have to parse it.

### D2. One visibility rule (`Viewer` and `VisibleTo`)

- **`Viewer`:** built once per request from the session's member. It holds `MemberId` and the set of
  `ProjectIds` from `project_members`.
- **`IQueryable<Change>.VisibleTo(viewer)`:** the change is visible when `project_id` is in the viewer's Projects, or
  `owner_member_id` is the viewer's own, or both are null.
- **Where it's applied:**
  - `ChangeCards.ListAsync` and `GetAsync` (which now take the viewer);
  - `ChangePages.GetAsync`, `RenderAsync`, `HistoryAsync` and `HistoryChoicesAsync`;
  - `MetricsReader.ForChangeAsync` and `ForTeamAsync`.
- **Not visible means not found,** the same as an unknown id. So an outsider can't tell a hidden change from a missing
  one (spec: Hidden changes don't exist).
- **The sweep test:** a single test signs in as an outsider and calls every change-reading endpoint for another
  Project's change, expecting 404 or empty. If a new endpoint is added and left out of the sweep, its review should
  catch that.
- **Alternative rejected: Postgres row-level security.** It's strong, but it needs a per-request `SET` of the
  viewer, which fits poorly with EF's pooled connections and the background services, and it's heavy for one rule.

### D3. Scoped live stream

- **Events carry their scope:** `ChangeEvent.Updated` (via its card) and `ChangeEvent.Archived` each carry the change's
  scope, `ProjectId` or `OwnerMemberId`.
- **Each subscription holds its viewer's scope.** `StreamAsync` drops events outside that scope before writing them.
- **Membership changes** are picked up by the existing keep-alive re-check (team-auth D11), which now also reloads
  the viewer's Project set. So joining or leaving a Project takes effect within one keep-alive.
- **Superseded in §11 (Emmz, 2026-11-10, after the 10.2 check).** Removing Sam while he watched left PAN's cards on
  his page. She asked for membership changes to take effect at once, for joining as well as removal. The keep-alive
  re-check stays as a backstop, and §11 adds:
  - **On the server:** after a membership write commits, the endpoint tells the broadcaster
    (`MembershipChanged(memberId, projectId, isMember)`). The broadcaster updates the scope of every open subscription
    of that member, and queues a `projects-changed` event `{ projectId, member: bool }` on them. Removal from the
    server ends the sessions, and with them the streams, as before.
  - **On the web:** a left Project's cards and filter choice are dropped. A joined one makes the dashboard refetch
    `/api/changes`. A change page whose Project was left shows not found.
  - **Not live:** history and metrics follow on their next load.
- **Alternative rejected: a channel per Project.** It would mean moving subscribers between channels when
  membership changes, which is more code than filtering 64-deep queues.

### D4. Reporting into a Project

- **The contract:** `ReportProgressArguments` gains an optional `project` (a string). The server parses it as a
  `Guid`. A malformed one gives the same error as an unknown one.
- **`ReportInput` gains `ProjectId`.** `ReportRecorder` resolves the scope first:
  - **A Project named:** it must exist, and the member must have a `project_members` row in any role. A bootstrap
    report has no member, so the Project only needs to exist. Otherwise it throws `ReportRefusedException`, which
    becomes an `McpException` naming the id and `list_projects`.
  - **No Project:** the personal scope of the member. A bootstrap report gets the both-null scope.
- **`FindOrAddChangeAsync`:** it keeps `FOR UPDATE`, and its `WHERE` gains `project_id IS NOT DISTINCT FROM {p} AND
  owner_member_id IS NOT DISTINCT FROM {m}`, matching the index.
- **The membership check** runs inside the report's transaction, so a removal that commits first refuses the report.
- **Linking mid-change** (spec change-visibility, Linking moves personal work). Emmz chose this on 2026-11-10, after
  a side review found that linking would split a card. With a Project named and the reporter a member, the lookup
  runs in order:
  1. **The Project's active change**, by repo and name, with `FOR UPDATE`. If it's found, the report updates it. If the
     reporter also has an active personal change of the same identity, that one gets `archived_at = now`, with no
     archive folder, and `retired_to_project_id` set to the Project. So it leaves the dashboard, and an `Archived` event
     goes to its owner.
     - **The history:** a retired copy stays in its owner's history. Its entry has a null `archiveFolder` and a
       `linkedTo` naming the Project (`{ id, code, name }`), so the row shows "Linked to PAN".
     - **The metrics:** the team metrics leave out retired copies (`retired_to_project_id IS NOT NULL`). Their work is
       measured on the Project's change, and their time ends at the link. A change's own metrics still answer for it.
     - Emmz chose both on 2026-11-10, after a side note found that the copy counted as a finished change and reached
       the history with a null folder.
  2. **Otherwise, the reporter's own active personal change**, by repo and name, with `FOR UPDATE`. If it's found, the
     report sets `project_id` and clears `owner_member_id`, then updates it. Its events, agents and documents hang off
     `change_id`, so they move with it, and its metrics stay whole.
  3. **Otherwise,** a new change in the Project.

  - **Live updates:** after a move, the `Updated` event carries the new Project scope, so the Project's members get
    the card, and so does the reporter, who belongs to the Project.
  - **Only the reporter's own personal change** is ever looked at, so linking can't pull in a teammate's personal
    work. Archived changes are never moved.
  - **Races:** two linked reports at once from one person serialise on the personal row's lock. Two from different
    people both hit the Project row (step 1) once it exists. The unique index catches a double create in step 3, and
    the recorder's existing retry on a unique violation re-runs the lookup.

### D5. Projects API

All of these are member-only. "Not a member" answers 404, so a Project's existence isn't leaked.

- **`GET /api/projects`:** my Projects, each with my role.
- **`POST /api/projects`** `{name, code}`: creates the Project and makes me its owner. A taken code is a 409.
- **`GET /api/projects/{id}`:** the Project, with its members (display name, email, role and joined date).
- **`PATCH /api/projects/{id}`** `{name}`: owner only.
- **`POST /api/projects/{id}/members`** `{email, role}`: owner only.
  - **The person:** it finds or creates the `Member` by `EmailKey`, reactivating a removed one, the same way as the old
    member invite.
  - **The membership:** it adds the row. One that's already there is a 409.
- **`PATCH /api/projects/{id}/members/{memberId}`** `{role}`: owner only.
- **`DELETE /api/projects/{id}/members/{memberId}`:** owner, or the member themselves to leave.
- **The last-owner rule:** each membership write starts with `SELECT … FROM projects WHERE id = … FOR UPDATE`, then
  counts the owners. So two owners demoting each other at once can't leave none.
- **Validation errors** are 400 with a message, in the style of the existing member endpoints.

### D6. `list_projects`

- **On the server:** an `[McpServerTool]` on `/mcp` (reporter policy), returning
  `ListProjectsResult { projects: [{ id, name, code, role }] }`. Both the text content and the structured content come
  from `Panopticon.Contracts`, as `report_progress` does. A bootstrap reporter gets every Project, with role `viewer`.
- **On the bridge:** the same tool, forwarded.
  - **The forwarder:** `ServerForwarder` gains a general `CallAsync(toolName, arguments, typeInfo)`, which both tools
    use.
  - **Errors:** configuration and transport errors become tool errors, as they do for `report_progress`.

### D7. `.panopticon.json`

- **Reading it:** `ProjectLink.ReadAsync(repoRoot)` reads `<root>/.panopticon.json` using a source-generated
  `JsonSerializerContext`, for AOT.
  - **No file:** null.
  - **Invalid JSON, or a `project` that's missing or not a Guid:** a `BridgeException` naming the file and the problem.
  - **Unknown fields:** ignored, so the tags change can add `tags` later. Only personal tags go in the machine
    config; project tags will live on the server.
- **When it's read:** on every report, with no cache, so editing the file takes effect at once.
- **Alternative rejected: `openspec/config.yaml`.** Emmz chose a file of Panopticon's own.

### D8. Members, rescoped

- **`GET /api/members`:** active members who share a Project with me (a join through `project_members`), and me.
- **`POST /api/members`:** removed. Its tests move to the Project invite.
- **`DELETE /api/members/{id}`:** allowed only when my `EmailKey` is among the configured `Panopticon:InitialMembers`.
  - **Where that comes from:** read at request time from the same parsing as `InitialMembers`, extracted to an
    `InitialMemberList` options class.
  - **The last-owner rule:** removal is refused, naming the Projects, when the member is the last owner of any. That
    keeps the "never without an owner" invariant.
  - **What it does:** besides today's effects, it deletes their `project_members` rows.
- **`GET /api/session`** gains `canRemoveMembers`, so the Members page shows Remove only to initial members.
- **Alternative rejected: an `is_admin` column.** It adds a second source of truth next to the config, which is
  already where operators say who runs the server.

### D9. Web

- **Routes:** `/projects` (the list, and a create form) and `/projects/{id}`, the Project page:
  - **The Project:** its name (editable by owners), code and id;
  - **The link file:** a copyable `.panopticon.json` block;
  - **Members:** with roles, and owners can change roles, remove, and invite (an email and a role);
  - **Leaving:** a "Leave this Project" button.
- **The header:** Projects joins the links (Projects, Members, History, Metrics, Tokens).
- **Cards, change pages and history rows** show the Project's code as a small label, or "Personal".
  - **The data:** the card gains `project: { id, code, name } | null` and `personal: boolean`. History entries and
    `ChangePage` reuse it.
- **The `project` filter key:** a Project id, or `personal`.
  - **The dashboard:** it filters in the browser on the card's project.
  - **The history and metrics:** `HistoryFilter` gains `Project` (a Guid) or `Personal`, parsed in `TryParse` like the
    others.
  - **The choices:** on the dashboard they come from the cards, and on the history from `HistoryChoices.Projects`.
- **The Members page:** the invite form is removed, with a line pointing to Projects. Remove shows only with
  `canRemoveMembers`.
- **Design:** the Projects pages and the code label get a `frontend-design` pass within `web/DESIGN.md`, recorded in a
  new "Projects" section.

### D10. Contracts and compatibility

- **The new `project` argument is optional,** so an older bridge still reports, into personal scope. That matches the
  spec scenario "Report from a CLI without Projects".
- **The bridge's `list_projects`:** an older server doesn't have the tool, so the bridge returns the server's
  "unknown tool" error as a tool error.

## Risks / Trade-offs

- **[A read path skips `VisibleTo`]** → One helper, and every reader takes a `Viewer` parameter, so leaving it out
  won't compile. The outsider sweep test covers every change endpoint, and the review checklist names it.
- **[Live events between a join and the next keep-alive]** → A newly added member waits up to 25 seconds to get
  live updates, and a removed one keeps them for up to 25 seconds. That's within the spec's "one keep-alive".
- **[Old rows go to their first reporter]** → This is local data only, before going live, and it's documented.
- **[The initial members list is config]** → Removing an address from the config removes that person's removal power
  at the next request. That's intended: config is the operator's word.
- **[`NULLS NOT DISTINCT`]** → It needs Postgres 15 or later. Compose and Railway run 17, and README already names 17.

## Migration Plan

1. **The migration** (`Projects`) creates `projects` and `project_members`, adds the two `changes` columns and the
   check, rebuilds both identity indexes, and backfills `owner_member_id` (D1). It runs with `--migrate`, as usual.
2. **After deploying,** each person creates or joins their Projects, and commits a `.panopticon.json` to each repo.
   Their next reports start the Project's changes. Older unlinked changes stay personal.
3. **Rollback:** the down migration drops the new tables and columns and restores the old indexes. Any two changes
   that now differ only by scope would collide, so the down migration deletes the non-personal duplicates first. This
   is acceptable before going live, and it's noted in the migration.

## Open Questions

None. Emmz's answers (2026-11-09) are in docs/DISCOVERY.md, under "Later → Projects → Decided".
