# Panopticon — Discovery

_Requirements only: what and why. No technology decisions here, except constraints confirmed as fixed._

## Problem & users

- **Users:** a **team**, using the dashboard **over the internet**. Several people sign in.
- **Team from day one.** Multiple sign-ins and per-user credentials are part of v1.
- **One team per deployment.** Everyone who signs in sees every agent. Another team runs its own
  instance, so there's no multi-tenancy.
- **Reporters:** **any MCP-capable agent** (Claude Code, Cursor, Codex, …), not just Claude Code.
- **The problem:** "I'm starting to use cloud VMs to run Claude, and it would be helpful to have a
  single overview of all my agents on multiple projects." Agents spread across machines and repos have
  no single place to see what they're doing.
- **Why now:** running agents on cloud VMs (instead of local terminals you can glance at) is new and
  growing.

## Outcomes & behaviours

Panopticon is two things:

1. **An online web app with an MCP endpoint** that agents use to report progress through OpenSpec
   changes.
2. **A CLI tool** to perform any necessary setup, sync an OpenSpec change's documents to the server,
   "that sort of thing".

### Web app — dashboard

- A **card for each change** being worked on. Emmz first said "a card for each connected agent". On
  reflection the card is the **change** (repo + change), and whichever agent(s) are working on it show
  inside the card. Each card shows:
  - the **repo** it is working in;
  - the **change** it is working on;
  - a **progress bar for the current section**, based on `tasks.md`;
  - a **progress bar for the overall change**, based on `tasks.md`.
- **Progress comes from the synced `tasks.md`.** The server parses its sections and checkboxes. The
  agent only says which change (and section) it is on and doesn't send any numbers itself.
- **Documents sync automatically on report.** Whenever an agent reports progress, the change's
  documents sync too, so the bars and drill-down stay as fresh as the last report.
- **A card stays until its change is done** (archived, see below), however long the agent is quiet.
- **Quiet cards look idle/stale**: e.g. dimmed, with "last seen 3h ago".
- **The dashboard updates live**: bars move and cards appear as agents report, with no refresh.
- **Reporting is authenticated per user.** Each agent/CLI reporter is tied to a team member through a
  credential issued at setup.
- **Finished changes stay browsable**, documents included, as history.
- **Filtering**: "choose a repo or a GitHub organisation or whatever". The dashboard can be narrowed
  to one repo, one org/owner, or other groupings.
- **Any git host.** Repos on GitHub, Azure DevOps, GitLab and others are supported, identified and
  grouped by host + owner + repo. Local-only repos with no remote are not required.
- **"Done" means archived in OpenSpec.** A card leaves the live view for history when `opsx:archive`
  moves the change into `archive/`. Having every task ticked isn't enough.
- **Metrics/analytics are in v1**, e.g. velocity and time per section. Which metrics exactly is
  still open (see below).
- **Clicking a card drills down** into a rendered view of **all the change's documents**: proposal,
  design, tasks, spec deltas and devlog notes, all browsable.

### Web app — look and feel

- It should **look fun but clean**.

### MCP endpoint

- Agents report progress through an OpenSpec change via the MCP endpoint.

### CLI

- Performs any necessary setup.
- Syncs an OpenSpec change's documents to the server.

## Scope

### In

- Web app for a signed-in team: one card per change (with its agents), section and overall progress
  bars, idle/stale state, live updates, filtering (repo, owner/org, …), drill-down into all of the
  change's rendered documents, browsable history of archived changes, and metrics/analytics.
- MCP endpoint for any MCP-capable agent to report progress, authenticated per user.
- CLI for setup (including issuing a reporter's credential) and syncing a change's documents.
- Repos on any git host.

### Explicitly out (v1)

- **Controlling agents** from the UI (pause, message, nudge). Panopticon observes, it doesn't steer.
- **Notifications** (Slack, email, …).
- **Editing documents** in the web UI. It's read-only.
- **Multi-tenancy.** One team per deployment.
- **Repos with no remote.**

### Later

Items above marked out of v1 are also candidates.

#### Projects (big)

_Source: Emmz, 2026-11-09._

**Built** in the `projects` change (2026-11-10): Projects with an id, a name and a code, linking a repo through a
committed `.panopticon.json`, the `list_projects` MCP tool, owner, editor and viewer roles with invites, and scoped
visibility. Editors manage the Project's tags since the `tags` change (below).

- **A Project:** it has three properties, an `Id`, a `Name` and a `Code`. For example: a UUIDv4, `Panopticon` and
  `PAN`.
- **Linking work:** a Project's `Id` goes in the local settings, which links that work to the Project.
- **The MCP tool:** the MCP server lists the Projects I own or have joined, with their `Id`s, so an agent can set
  up the link.
- **Members:** I can invite people to join a Project. There are three levels of membership:
  - **`owner`:** can invite and remove people.
  - **`editor`:** can edit the Project's tags.
  - **`viewer`:** can see the cards, use the MCP server, and so on.

#### Tags (small)

_Source: Emmz, 2026-11-09._

**Built** in the `tags` change (2026-11-10). Each Project has a list of tags, managed by its owners and editors on the
Project page, and they put them on changes from the change page, or agents do it with the `tag_change` MCP tool,
using existing tags only. Personal tags are kept per machine with `panopticon tags`, carried by reports, and seen
only by their owner. Chips show on cards, change pages and history rows, and edits show live. A multi-select Tags
filter on the dashboard, history and metrics matches every chosen tag, or a key alone, and goes in the address.
Not built: the Project page's Tags list doesn't update live (deferred by Emmz, 2026-11-10).

- **What a tag is:** an arbitrary dimension to filter on. A tag has an optional value, so there could be a `dev` tag
  and a `language: csharp` tag.
- **Personal tags:** these are kept in the local settings and sent with reports. Only I can see my own tags.
- **Project tags:** for example, `PAN/bug` or `PAN/area: onboarding`. They're managed on the Project's properties
  page, and every member of the Project can see them.

#### Decided (Emmz, 2026-11-09)

- **Needed before going live.** Emmz will work on three Projects with different people, and their work mustn't mix.
- **Who sees what:** only a Project's members see its cards. Work linked to no Project is seen only by the person who
  reported it.
- **The link:** the Project `Id` lives in a file committed to the repo (e.g. `.panopticon.json`), so everyone working
  in that repo reports into the same Project. Personal tags, being one person's own, live in the machine's CLI
  config.
- **Invites:** an owner invites by email address. That also lets the person sign in, if they couldn't already. So
  server-wide membership becomes "can sign in", and Projects decide what each person sees.
- **Reporting:** all three roles can report into a Project. A viewer's agents work in it, and a viewer only can't
  manage its tags or people.
- **Two changes:** Projects first (what going live needs), then tags.
- **Creating:** anyone who can sign in can create a Project, and becomes its owner.
- **The code:** unique on the server, and fixed once created, so project tags can rely on it. The name can change.

#### Open questions (not yet asked)

- **Tags in reports:** answered (Emmz, 2026-11-10). Project tags are put on a change on the dashboard, by the
  Project's owners and editors, choosing from the tags the Project defines (or adding a new one from the picker).
  Their agents can set them too, with a `tag_change` MCP tool, but only from the existing tags: reports don't carry
  project tags, and an agent can't create one. Personal tags
  come in with reports from the CLI config, and they belong to the change, for the person who sent them: a change has
  the union of the personal tags that person has reported on it, and only that person sees them.
- **The code:** answered, and built. It's unique on the server, regardless of case, and can't change.
- **Owners:** answered, and built. A Project can have several owners, and it always keeps one, so the last owner
  can't leave. Any member of the deployment can create a Project.
- **Viewers and the MCP server:** answered, and built. Viewers report into the Project.
- **Filters:** answered (Emmz, 2026-11-10). Yes, as one multi-select Tags filter on the dashboard, history and
  metrics. It lists the personal and project tags you can see. A `key: value` tag matches either on its key alone or
  on its key and value. The filter goes in the URL like the others.

## Hard constraints

- **Agents report via MCP.** The reporting interface is an MCP endpoint. _Source: Emmz — the agents
  that will use it speak MCP (required integration)._
- **Progress is derived from OpenSpec's `tasks.md`.** Section and overall progress come from its
  sections and checkboxes. _Source: Emmz — required integration with the OpenSpec format._
- **UI design is done with the `frontend-design` skill.** _Source: Emmz — process mandate._

## Success criteria

- **A glance replaces SSH.** Emmz checks Panopticon instead of SSHing into VMs or opening terminals
  to see what agents are doing.
- **Stalls are spotted fast.** An idle or stuck agent is obvious at a glance.
- **Setup is quick.** A new repo or VM is reporting within minutes, using the CLI.
- **The team uses it daily.** Teammates open it routinely.

## Open questions & ambiguities

- ~~**Which metrics?**~~ _Resolved by the `change-metrics` change (2026-11-09): Emmz chose time per
  section and time to archive. They show in a panel on the change page, and team-wide on a `/metrics`
  page (counts, medians, a weekly chart and the slowest sections)._
- ~~**How does the server learn of an archive?**~~ _Resolved by ADR-0001: the CLI's local MCP bridge
  notices when the change moves to `archive/` and reports it._
- ~~**"Or whatever"**: other filters wanted?~~ _Resolved by the `filters-and-idle-cards` change
  (2026-11-09): repo, owner/org, person, machine, agent type and (dashboard only) status, plus Tags
  since the `tags` change. Filters combine and go in the URL._
- ~~**How long is "quiet"?**~~ _Resolved by the `filters-and-idle-cards` change: idle after 30
  minutes, stale after 4 hours by default, set by `Panopticon:Activity` on the server._
- ~~**"That sort of thing"**: what else does the CLI do beyond setup and sync?~~ _Closed by Emmz
  (2026-11-11): what was built answers it. That's login, linking a repo to a Project, personal tags,
  the MCP bridge and archive reporting. See `docs/cli.md`._
