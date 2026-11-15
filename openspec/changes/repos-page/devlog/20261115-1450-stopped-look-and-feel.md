# Stopped: §4 look and feel, with Emmz in a real browser

§1–§3 are committed (`ecfe8a8`), and task 4.1 is unticked.

## Set-up (this session)

- The web bundle is built (`make web-build`).
- A Development server is running on `http://localhost:7481` against a scratch database, `panopticon_repos_check`,
  in the compose Postgres, so Emmz's own dev data isn't touched. It was started with `env ASPNETCORE_ENVIRONMENT=Development
  Urls=http://localhost:7481 Panopticon__PublicUrl=http://localhost:7481 ConnectionStrings__Panopticon=…;Database=panopticon_repos_check;…
  dotnet run --project src/Panopticon.Server -c Release --no-launch-profile`, with its log in the session scratchpad.
- Seeded through MCP with the bootstrap token (seen by every member) by the scratchpad's `seed-repos.mjs`:
  - `dev.azure.com cpointrepos/SMART-NOVA/NOVA-INFRA`: 8 archived and 1 active.
  - `github.com emmz-makes-stuff/panopticon-skills`: 1 archived.
  - `github.com emmz-makes-stuff/panopticon`: 2 active and 1 archived.
- Sign in as `dev@localhost.test`. The link is in the server log.

## Question

Does the Repos page look right in light and dark? Do the repo links look right on the cards, History, the change
page and metrics? Do they open the right GitHub and Azure DevOps pages in a new tab?

## Next

Record Emmz's answers in a devlog note, then fix anything she raises. Afterwards, stop the server and drop
`panopticon_repos_check` (`docker exec panopticon-postgres-1 psql -U panopticon -d panopticon -c "DROP DATABASE
panopticon_repos_check"`). Then tick 4.1 and finish the change (the whole-change review, then offer /code-review).
