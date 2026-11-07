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

_Nothing named yet. Items above marked out of v1 are candidates._

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

- **Which metrics?** Metrics are in v1, but not defined yet. Candidates: velocity (tasks/day), time
  per section, time to archive, per-person or per-repo throughput, trends over history. Where do they
  live: on cards, in the drill-down, or on a separate view?
- ~~**How does the server learn of an archive?**~~ _Resolved by ADR-0001: the CLI's local MCP bridge
  notices when the change moves to `archive/` and reports it._
- **"Or whatever"**: other filters wanted, e.g. by person, by agent type, by machine, by status
  (active/idle)?
- **How long is "quiet"?** What threshold marks a card idle/stale?
- **"That sort of thing"**: what else does the CLI do beyond setup and sync?
