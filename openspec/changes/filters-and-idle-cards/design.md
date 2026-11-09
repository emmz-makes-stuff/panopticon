# Design

## Context

See proposal.md for the why. Today:
- **Dashboard (`/`):** loads every active card from `GET /api/changes`, then applies `change-updated` and
  `change-archived` events from `/api/events`. `views/cards.ts` already re-reads each card's relative time every
  30 s (`app.updateTimes()`).
- **Router:** since change-documents-and-history §10 it supports `ViewHandle.update(params)`, so a view can react
  to a query-only navigation without being rebuilt.
- **History:** `GET /api/history` pages archived changes by `before`, 50 at a time (`ChangePages.HistoryAsync`).
- **Identity:** cards and history entries carry agents as `CardAgent(Client, Machine, LastReportedAt, Member)`,
  where `Member` is a display name only. Repos and agents have lower-cased `…Key` columns, which identity already
  uses for case-insensitive matching.
- **No data model change:** quiet state needs only `Change.LastReportedAt`, and every filter dimension already
  exists in `repos`, `agents` and `change_agents`.

## Goals / Non-Goals

**Goals:**
- Quiet state computed in the browser from the last report time, so it moves on with the clock between reports.
- One filter model (parse, serialize, match) shared by the dashboard and the history.
- Live updates and history paging stay correct under filters.

**Non-Goals:**
- **Multi-select filters** (two repos at once). One value per dimension. A later change can widen this.
- **Saved or default filters per member.** The address is the only state (Emmz's choice).
- **Per-agent quiet state.** The card's state follows the change's last report; agent rows keep their own time.
- **Server-side filtering of `GET /api/changes` and the live stream.** See D3.
- **Clicking a card's repo or agent to filter.** That's a possible follow-up; the filter bar is the one way in.

## Decisions

### D1. Quiet thresholds: `Panopticon:Activity` options, served by `GET /api/settings`

- **The options:** `ActivityOptions { IdleAfter = 00:30:00, StaleAfter = 04:00:00 }`, bound from
  `Panopticon:Activity` like the other option sections. They're validated on start: both positive, and
  `StaleAfter > IdleAfter`, with a message naming both keys.
- **The endpoint:** `GET /api/settings` (member-only) returns `{ "idleAfterSeconds": 1800, "staleAfterSeconds":
  14400 }`. The web app fetches it once, when the signed-in shell starts, and passes it on in `Env`.
- **Alternatives considered:**
  - **The server sends each card's state.** Rejected: the state must change while no report arrives, which would
    mean a server timer pushing events.
  - **Thresholds in `/api/session`.** Rejected: the session describes the member, not the deployment.
  - **Hard-coding the thresholds in the bundle.** Rejected: Emmz chose settings.

### D2. State is a pure function of `(lastReportedAt, now, thresholds)`

`quietState(card, now, settings) → "active" | "idle" | "stale"`. It's applied at render and on the existing 30 s
tick, so a card goes idle within 30 s of the threshold, inside the spec's "within a minute".
- **On the card:** the state goes on as a class (`card--idle`, `card--stale`) plus a text label: "Idle", or
  "Stale · last seen 5h ago" in place of the plain time. The label is the accessible signal, so the look isn't
  colour alone.
- **Clock skew:** a future `lastReportedAt` already reads "just now", and here it reads as active.

### D3. Dashboard filters on the client; history filters on the server

- **Dashboard:** every active card is already in the browser, and the stream sends every change. So the app keeps
  the full set and shows the matching subset, re-evaluated on each event, each tick (for status) and each filter
  change.
  - Filtering on the server would mean filtered streams, and a refetch on every filter change, for a list that is
    small (one team's active changes).
  - The change count reads "3 changes", or "3 of 7 changes" with a hint that filters hide the rest.
- **History:** this is unbounded and paged, so filtering has to happen in the query. `GET /api/history` gains
  optional `repo`, `owner`, `member`, `machine` and `client` parameters.
  - **Matching:** they become `Where` clauses on `cards.Query()`. The repo and owner parameters compare the
    `…Key` columns. The member, machine and client parameters use `c.Agents.Any(a => …)` on `MemberId`,
    `MachineNameKey` and `ClientNameKey`.
  - **Paging:** the `before` cursor works unchanged within a filter.
  - **Bad values:** an invalid `member` (not a GUID) or a malformed `repo`/`owner` gives a 400 naming the
    parameter.

### D4. The address format

Query keys, one value each, in this order when the app writes them:
- `repo=<host>/<owner>/<name>`: the first segment is the host, the last is the name, and the rest is the owner
  (which may contain '/');
- `owner=<host>/<owner>`;
- `member=<member id>`;
- `machine=<name>`;
- `client=<name>`;
- `status=active|idle|stale` (dashboard only; ignored by the history).

`URLSearchParams` does the encoding. Unknown keys are ignored. A value that doesn't parse is dropped, and the
filter bar says so, much as an unknown `?doc=` does. Writing the keys in a fixed order keeps the addresses stable,
so the same filters always give the same link.
- **Changing a filter** calls `navigate("/?…")`. The router sees the same path and view and calls the cards
  view's new `update(params)` (it returns a `ViewHandle` now, like the change page). So there's no remount, no
  refetch and no reconnect, while Back and Forward still step through filter choices.
- **Matching** (`filters.ts`): `parseFilters(params)`, `toQuery(filters)` and `matches(card, filters, state)`,
  shared by both pages. For the history, `toQuery` also builds the API query.

### D5. Filter choices

- **Dashboard:** `filterChoices(cards)` derives the distinct repos, owners, members (`memberId` → the latest
  display name), machines and clients from the current full set. Labels sort case-insensitively, and the choices
  recompute whenever a card is upserted or removed. A chosen value that no current card has stays offered (marked
  "no changes"), so the filter bar can still show and clear it.
- **History:** `GET /api/history/filters` returns the same shape (`repos`, `owners`, `members[{id, displayName}]`,
  `machines`, `clients`), over archived changes only, with distinct values from the same joins as D3. It's
  fetched once per history view.

### D6. `CardAgent.MemberId`

`CardAgent` gains `Guid? MemberId`, set in `ChangeCards.ToCard` from `Agent.MemberId`. It's additive JSON
(`memberId`, null for a bootstrap-token agent). The person filter matches on it, and its label uses `Member`.

### D7. The filter bar's UI

One bar above the grid (and above the history list):
- a `<select>` per dimension, each with an "All …" first option, labelled for assistive technology;
- the status select on the dashboard only;
- a "Clear filters" button, shown when any filter is set.

On narrow screens the bar wraps, or collapses behind a "Filters (2)" toggle; the `frontend-design` pass decides
which, within `web/DESIGN.md`. Quiet-state styling (idle muted, stale dimmed) is part of the same pass, keeping the
lit-cell motif readable when dimmed.

## Risks / Trade-offs

- **[Client clock skew]** A browser clock far off the server's skews the state. → It's the same exposure the
  relative times already have. If it matters later, the settings endpoint can return the server's `now`.
- **[Many choices]** A long-lived team could have dozens of machines in a select. → One select per dimension
  scales to a few hundred options. Search can come later if needed.
- **[History filter cost]** `Agents.Any` subqueries on a large history. → The `change_agents` and `agents` keys are
  indexed by their existing primary and unique keys. The history is small in v1, so revisit with metrics.
- **[Stale filter values in shared links]** A link filtering by a member who has since been removed. → The value
  stays in the bar as "unknown member" with a clear button, and nothing matches.

## Migration Plan

Additive only: a new options section with defaults, a new endpoint, new optional query parameters, and a new JSON
field. There's no migration, and old browsers ignore `memberId`. Rollback is redeploying the previous image.
