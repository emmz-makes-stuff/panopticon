# Design

## Context

See proposal.md for why. What exists today:

- **Active changes** come from `GET /api/changes` (`ChangeCards.ListAsync`, `ArchivedAt == null`, `.VisibleTo(viewer)`),
  kept live by `/api/events`.
- **Archived changes** come from `GET /api/history` (`ChangePages.HistoryAsync`). It's paged 50 at a time and filtered
  by `repo=<host>/<owner>/<name>` among others.
- **Repo identity** is stored as `host`, `owner` and `name`, already normalised by the CLI's `GitRemote`: the host is
  lowercased, `ssh.dev.azure.com` becomes `dev.azure.com`, and `_git` and `v3` are dropped. Credentials and ports are
  never kept. The browser sees it as `CardRepo { host, owner, name }`.
- **Repo text** is shown as plain text in `card.ts` (host line, then `owner/name`), `views/history.ts`
  (`host owner/name`), `views/change.ts` (`renderHead`) and `views/metrics.ts` (`owner/name`). No endpoint lists repos.
- **Visibility** is the `VisibleTo` query filter in `Projects/Viewer.cs`.

## Goals / Non-Goals

**Goals:**
- One request gives the whole Repos page.
- One function builds every repo link, so the rule lives in one place.

**Non-Goals:**
- A page per repo, or a filter bar on the Repos page. History already filters by repo, and the Repos page links there.
- Live updates on the Repos page. It loads once per visit, like History.
- Configurable link rules or self-hosted ports. See Risks.

## Decisions

### D1. One endpoint, `GET /api/repos`, grouped on the server

The response is `{ repos: RepoSummary[] }`, where each summary is:

```
{ repo: { host, owner, name },
  latestActivityAt,
  active:   [{ changeId, change, project, personal, lastReportedAt }],
  archived: [{ changeId, change, project, personal, archivedAt }],
  moreArchived }
```

The server runs two queries under `VisibleTo(viewer)`: active changes, and archived changes without retired copies (the
same exclusion the metrics use). It groups both by repo, keeps the five newest archived per repo, counts the rest into
`moreArchived`, and sorts as the spec says. It lives next to `HistoryAsync` in `ChangePages` and is mapped in
`ChangeEndpoints`.

*Alternative:* the browser merges `/api/changes` with the history. History is paged 50 at a time, so the browser would
have to page through all of it to find each repo's newest five and its count. Grouping on the server is one round trip,
and it counts exactly.

*Alternative:* a `repo` dimension on `/api/history`. That gives archived changes only, and a flat page, not groups.

The archived query fetches only the columns the summary needs. A per-repo "top five" in SQL (a window function) isn't
worth it at this scale: one member's visible archive is hundreds of rows, not millions. If that changes, the endpoint
can move to `ROW_NUMBER()` without changing its contract.

### D2. `repoWebUrl(repo)` in `web/src/links.ts`

```
dev.azure.com, *.visualstudio.com  -->  https://<host>/<owner…>/_git/<name>
any other host                     -->  https://<host>/<owner…>/<name>
```

Each `/`-separated owner segment and the name go through `encodeURIComponent`. The host is used as stored, since the CLI
already lowercased it and dropped user info and the port. The rule lives in the browser, not the API, because it's pure
presentation over data the browser already has: the cards, History and metrics need no contract change.

*Alternative:* the CLI sends the remote's web URL. It knows the original URL, but that means a contract and CLI release
for data we can derive, and old reports would have no link.

### D3. `repoLink(repo, text)` in `web/src/dom.ts`

This makes an `<a class="repo-link" href=… target="_blank" rel="noopener noreferrer">` with `title="Open <owner>/<name>
on <host>"`. It's an ordinary link, not a `data-route` one, so the router leaves it alone. Every view calls it in place of
its plain `owner/name` span, keeping the span's existing class for layout.

- **Card:** `owner/name` on the repo line becomes the link. The host line stays text with its plate. The change name
  stays the card's only route link (change-documents-and-history D11).
- **History row and change page:** the `owner/name` part becomes the link, and the host stays text before it. History's
  single `host owner/name` span splits in two to allow this.
- **Metrics slowest sections:** `owner/name` becomes the link.

### D4. Look: muted, with the header's `--rule` underline

The repo text keeps its small muted colour and gets the same `--rule` underline as the header links (web/DESIGN.md
"Header"). The underline shows it's a link without competing with the change name. There's no hover effect (principle 4).
The focus ring is the shared one. DESIGN.md gets a "Repo links" rule, and the Repos page section.

### D5. The Repos view, `web/src/views/repos.ts`

It's routed at `/repos` in `shell.ts` and linked in the header after History. It fetches `/api/repos` once. Each repo is
a `section` with an `h2` that holds the host (muted text) and the `owner/name` repo link. Under it is a list of changes:
the change name as a route link (Gabarito 700, like History), the Project plate or "Personal", then "Reported 2h ago"
(the same relative time as the cards) or "Archived 14 Nov 2026" (the same date format as History). Active changes show
no other state. The cards page has the bars. With `moreArchived > 0`, the list ends with a route link "N more in History"
to `/history?repo=<host>/<owner>/<name>` (the `repoValue` form the History filter already reads). Empty, loading and
error states follow History's.

## Risks / Trade-offs

- [A self-hosted server on a custom port, or one served only over `http`, gets a wrong link, because the port and scheme
  aren't stored] → Accepted for now: it's rare, and the link only fails to open. A later change could have the CLI send
  the web URL (D2's alternative) if someone needs it.
- [An Azure DevOps Server (on-premises) host uses `/<collection>/<project>/_git/<name>` on its own domain, and gets the
  default rule] → Same as above. Add hosts to D2's list if they come up.
- [The Repos page doesn't update live] → It's a browsing page like History. Reloading or coming back to it fetches again.
- [A member with a large archive makes the archived query heavier] → D1's columns-only query, and the way out to a
  window function.

## Migration Plan

Nothing to migrate: a new read-only endpoint and dashboard changes, shipped in the next release. Rolling back is
reverting the release.
