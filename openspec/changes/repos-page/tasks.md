# Tasks

## 1. Server: `GET /api/repos`

- [x] 1.1 Add the `RepoSummary` response types and `ChangePages.ReposAsync(viewer)` (design D1). It queries active and archived changes under `VisibleTo(viewer)`, leaves out retired copies, groups by repo, keeps the five newest archived per repo with a `moreArchived` count, and sorts by latest activity, then host, owner and name. Map it as `GET /api/repos` in `ChangeEndpoints`, signed-in only. Verify with endpoint tests for the `repos` spec's "Repos list", "Repos order" and "Archived changes on the Repos page" scenarios: a repo with active and archived changes, Sam not seeing a repo with no visible change, an empty list, a 401 signed out, the ordering by archive time, eight archived changes giving five and `moreArchived: 3`, and a retired copy left out.
- [x] 1.2 Fix the test clock time bomb (Emmz, 2026-11-15): the browser test client's cookie container judged cookie expiry by the real clock, so sign-in cookies set at `ManualClock`'s 2026-11-08 start expired for real on 2026-11-15, failing 36 server and 7 CLI tests on `main` too. Add `ServerClockCookieHandler`, which moves each `Set-Cookie` expiry from the server's clock onto the real one, and use it in `PanopticonFactory.CreateBrowserClient`. Verify: `make test` passes on a date past the clock's start plus seven days (today).

## 2. Dashboard: repo links

- [x] 2.1 Add `repoWebUrl(repo)` to `web/src/links.ts` (design D2) and `repoLink(repo, text)` to `web/src/dom.ts` (D3). Verify with unit tests for the "Repo web addresses" scenarios (Azure DevOps, a `*.visualstudio.com` host, a nested GitLab group, GitHub, a name needing encoding), and that the link has `target="_blank"`, `rel="noopener noreferrer"`, a title, and no `data-route`.
- [x] 2.2 Use `repoLink` for the repo's `owner/name` in `card.ts`, `views/history.ts` (splitting its host and repo text), `views/change.ts` and `views/metrics.ts`, and add the `repo-link` style (D4). Verify the "A card's repo" scenario in `card.test.ts`, with matching link assertions in the History, change page and metrics tests, and that the filter selects still have no link.
- [x] 2.3 Add the "Repo links" rule to `web/DESIGN.md` (D4). Verify it names where the link appears and its look.

## 3. Dashboard: the Repos page

- [x] 3.1 Add the `/api/repos` types and `fetchRepos` to `web/src/types.ts` and `web/src/api.ts`, and the view `web/src/views/repos.ts` (design D5). Route it at `/repos` in `shell.ts`, and add the header link after History. Verify with `views/repos.test.ts` through `startTestShell`: the "Repos list" scenarios (ordering within a repo, route links to `/changes/<id>`, repo links, the empty state, signed out and returning to `/repos`), "3 more in History" linking to `/history?repo=…`, and the "Opening the Repos page" scenario from the header.
- [x] 3.2 Add the Repos page to `web/DESIGN.md` (its section, and Repos in the header list), and a Repos section to `docs/using.md` (what the page shows, and that repo names link to the repo's web page). Verify the header list in DESIGN.md matches `shell.ts`, and `docs/using.md` matches the `repos` spec.

## 4. Look and feel (human verification)

- [ ] 4.1 Build the web bundle and run the server locally with a few changes reported across two repos, one of them on `dev.azure.com`, and one with more than five archived changes. Give Emmz a copy-pasteable recipe. Verify with Emmz in a real browser: the Repos page's layout in light and dark, the repo links' look on the cards, History, the change page and metrics, and that the links open the right GitHub and Azure DevOps pages in a new tab. Record her answers in a devlog note.
