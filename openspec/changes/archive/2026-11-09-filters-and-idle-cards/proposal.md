# Proposal

## Why

The dashboard is one flat list of every active change, and a card that went quiet hours ago looks the same as one
reported a minute ago. Two of the discovery's success criteria depend on fixing that: "a glance replaces SSH" needs
the board narrowed to the repo or team you're looking at, and "stalls are spotted fast" needs quiet cards to stand
out. Both are in v1 scope ("idle/stale state", "filtering (repo, owner/org, …)") and are next on the roadmap now that
change pages and history have landed.

## What Changes

- **Quiet cards.** A card is *active*, *idle* or *stale* depending on how long ago its change was last reported:
  idle after 30 minutes, stale after 4 hours by default. Each state looks distinct and is named in text, not by
  colour alone. A stale card shows "last seen 4h ago". The state moves on as time passes, with no new report, and
  goes back to active as soon as a report arrives. The two thresholds are server settings.
- **Filters on the dashboard and the history.** Either page can be narrowed by:
  - repo (host, owner and name), or owner/org (host and owner);
  - person (a team member, through their agents);
  - machine;
  - agent type (the MCP client name, e.g. `claude-code`);
  - and, on the dashboard only, status (active, idle, stale).

  Filters combine: a change must match all of them. The chosen filters live in the URL query, so a filtered view
  can be shared, survives a reload, and works with Back and Forward. On the dashboard, live updates respect the
  filter: a report on a change outside it doesn't add a card, and a card leaves the view when it stops matching a
  status filter. The history is filtered on the server, so its paging still works.
- **Filter choices come from what exists.** The dashboard offers the repos, owners, members, machines and agent
  types on its current cards. The history asks the server for those found among archived changes.
- **Cards identify members by id.** Each agent on a card, and in a history entry, carries its member's id as well
  as the display name, so a person filter survives a rename. This is an additive API field.

## Capabilities

### New Capabilities

- `change-filters`: narrowing the dashboard and the history by repo, owner/org, person, machine, agent type and
  (dashboard only) status. Covers how filters combine, how they appear in the URL, how live updates and paging
  respect them, and where the choices come from.

### Modified Capabilities

- `live-dashboard`: cards gain the idle and stale states, with configurable thresholds ("Card content" changes to
  say how a card shows its state, and a new "Quiet cards" requirement).

## Impact

- **Server:**
  - new options section `Panopticon:Activity` (`IdleAfter`, `StaleAfter`), served to the web app;
  - `CardAgent` gains `MemberId`;
  - `GET /api/history` takes filter parameters;
  - a new `GET /api/history/filters` lists the choices among archived changes.
- **Web:**
  - a filter bar on the dashboard and the history;
  - URL query handling through the router's in-place `update`;
  - card state rendering and its timer;
  - `frontend-design` pass and a `web/DESIGN.md` section for the filter bar and quiet states.
- **Docs:** README settings table (the two thresholds).
- **Unchanged:** no change to the CLI, the MCP contract or the data model (no migration). Idle state is computed
  from `LastReportedAt`, which already exists.
