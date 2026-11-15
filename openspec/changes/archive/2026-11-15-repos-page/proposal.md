# Proposal

## Why

The dashboard is organised by change: the cards show what's active and History shows what's archived. The repo is just a
line of text on each. There's no way to see one repo's work in one place, and no quick way from a change to the repo's
own web page, such as GitHub or Azure DevOps.

## What Changes

- A **Repos** page at `/repos`, linked in the header after History. It lists every repo the member can see, newest
  activity first. Under each repo are its active changes, then its most recently archived ones. Each change links to its
  change page. A repo with more archived changes than the page shows links to History, filtered to that repo.
- A new `GET /api/repos` endpoint returns that list, with the same visibility rules as the cards and History.
- **Repo links:** every place the dashboard shows a repo's owner and name links it to the repo's web page, in a new tab.
  That's the cards, History rows, the change page, the metrics page's slowest sections and the Repos page. The link is
  built from the host, owner and name we already store: `https://<host>/<owner>/_git/<name>` for Azure DevOps, and
  `https://<host>/<owner>/<name>` for every other host. The filter chips and selects stay plain filters.

## Capabilities

### New Capabilities

- `repos`: the Repos page, the list it shows, and links from repo mentions to the repo's web page.

### Modified Capabilities

None. The cards, History, change page and metrics keep their requirements. The `repos` spec adds the link to the repo
text each of them already shows.

## Impact

- **Server:** a new `GET /api/repos` endpoint in `src/Panopticon.Server` (read-only, no schema change) and its tests.
- **Dashboard:** a new view `web/src/views/repos.ts`, a route and header link in `web/src/shell.ts`, a repo-link helper,
  and the link in `card.ts`, `views/history.ts`, `views/change.ts` and `views/metrics.ts`, with their tests.
- **Docs:** `web/DESIGN.md` (the header list, a Repos section, the repo link's look) and `docs/using.md`.
- No CLI, plugin or migration changes.
