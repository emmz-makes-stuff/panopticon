# Proposal

## Why

Emmz is about to work on three Projects, each with different people, on one Panopticon server. Today everyone who can
sign in sees every card, every change page, the history and the metrics, and any member can invite or remove anyone.
The Projects' work and people would all be mixed together, so the server can't go live like this (docs/DISCOVERY.md,
"Later → Projects").

## What Changes

- **Projects:** a Project has an `Id` (a UUIDv4), a `Name` and a `Code` (e.g. `PAN`).
  - **Creating one:** anyone who can sign in can create a Project, and becomes its owner.
  - **The code:** unique on the server and fixed once created, so the next change's project tags (`PAN/bug`) can rely
    on it. The name can change.
- **Membership:** three roles.
  - **`owner`:** invites and removes people, and changes their roles. There can be several owners, but never none:
    the last owner can't leave or be demoted.
  - **`editor`:** will edit the Project's tags (the next change). In this change it's a viewer.
  - **`viewer`:** sees the Project's cards, change pages, history and metrics, and reports into it.
- **Invites by email:** an owner invites an address with a role. If that address can't sign in yet, it now can.
  **BREAKING:** server-wide membership becomes just "can sign in", and the Projects you belong to decide what you see.
- **A Project page:** its name, code and Id, and its members with their roles. Owners invite, remove and change roles
  there.
- **Linking work:** a `.panopticon.json` committed at the repo root names the Project, by its `Id`.
  - **The bridge:** it reads the file and sends the `Id` with each report.
  - **The server:** it refuses a report into a Project the reporter isn't a member of, saying why.
  - **Work with no link file:** it's still accepted, and only the person who reported it sees it.
- **A `list_projects` MCP tool**, on the server and on the bridge. It lists the Projects you own or have joined, with
  their `Id`, name, code and your role, so an agent can write `.panopticon.json` itself.
- **Scoping (BREAKING):** the dashboard, change pages, documents, the history, the metrics, filter choices and the live
  stream show only:
  - the changes in Projects you belong to;
  - unlinked changes you reported.

  A change you can't see answers 404, as though it didn't exist.
- **Change identity:** a change is now identified by its Project (or, when unlinked, its reporter) as well as by repo
  and name. So the same repo and change name in two scopes are two separate changes, and never mix.
- **In the views:** cards, change pages and history rows show the Project's code. The dashboard, history and metrics
  gain a **Project** filter.
- **The Members page (BREAKING):** it lists only the people who share a Project with you, and you can still rename
  yourself there.
  - **Moves to the Project page:** inviting and removing.
  - **Removing someone from the server:** only the initial members (`Panopticon:InitialMembers`) can.
- **Existing data:** the server isn't live, so changes reported before this change become unlinked and stay visible to
  their reporters only. Re-reporting from a repo with a `.panopticon.json` starts the Project's own changes.

Not in this change: personal and project tags, and tags in the filters (the next change). Also left out: a
`panopticon link` CLI command (the MCP tool covers linking), invitation emails (as today, an invite only lets someone
sign in), and deleting a Project.

## Capabilities

### New Capabilities

- `projects`: Projects (`Id`, name, code), creating them, the owner/editor/viewer roles, invites by email, the Project
  page, and the `list_projects` MCP tool.
- `change-visibility`: who can see a change. A change is in a Project's scope or its reporter's, and every view, API
  and live update shows only the changes in the viewer's scopes.

### Modified Capabilities

- `team-members`:
  - **Inviting:** moves to Projects.
  - **The member list:** limited to people who share a Project with you.
  - **Removing someone from the server:** only the initial members can.
- `progress-reporting`: a report may name a Project, and it's refused when the reporter isn't a member. A change's
  identity now includes its scope, the Project or the reporter.
- `cli-bridge`: the bridge reads `.panopticon.json` at the repo root and sends its Project `Id`, refuses a malformed
  file, and serves `list_projects`.
- `live-dashboard`: cards show their Project's code.
- `change-history`: history rows show their Project's code.
- `change-filters`: a Project filter on the dashboard and the history, and its choices.
- `change-metrics`: the metrics page takes the Project filter too.

## Impact

- **Server:**
  - **New data:** `projects` and `project_members` tables. `changes` gains `project_id` and `owner_member_id`, the
    change identity index is rebuilt, and there's a migration.
  - **Visibility:** a visibility filter applied in `ChangeCards`, `ChangePages`, `MetricsReader`, the history choices
    and the change endpoints.
  - **Live stream:** each event carries its change's scope, and each subscriber gets only events in its own scopes. A
    membership change is picked up within one keep-alive.
  - **Project endpoints:** the Project API and the `list_projects` MCP tool.
  - **Members:** the member endpoints are rescoped, and an initial-member check is added for server-wide removal.
- **Contracts:** `ReportProgressArguments` gains an optional `project`, and there are new list-projects contracts. This
  is additive on the wire, so an older bridge still reports, unlinked.
- **CLI:** the bridge reads `.panopticon.json` and forwards `list_projects`. `docs/cli.md` documents the file.
- **Web:**
  - **New views:** a Projects list and a Project page.
  - **Changed:** the Project code on cards and history rows, and the Project filter (dashboard, history and metrics).
  - **The Members page:** rescoped, with its invite form moved.
  - **The header:** a Projects link.
- **Docs:** the README (Projects, linking a repo, who sees what) and `docs/DISCOVERY.md`.
- **No new dependencies.**
