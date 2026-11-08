# Stopped: §8.3 end-to-end check needs Emmz watching

<!-- Section: 8 · Tasks: 8.1–8.3 · Base: ab35c46 · Status: stopped, §8 uncommitted on purpose -->

**Why stopped:** 8.3 is a real agent (Claude Code) driving `panopticon mcp`, with bars moving live in a
real browser. Both are human-verification points. 8.1 and 8.2 are done but deliberately **unticked**, so
that ticking them during the check moves the bars. Everything is uncommitted: `Dockerfile`,
`.dockerignore`, `README.md`, `local/container.env`.

## What landed (uncommitted)

- **8.1 `Dockerfile`:**
  - Stages: `node:24.13.1-alpine` (`npm ci`, then `npm run build` into wwwroot), then
    `dotnet/sdk:10.0.403` (restore from the csproj files first, then publish), then
    `dotnet/aspnet:10.0.14` (runs as `$APP_UID`, on 8080).
  - The runtime stage installs **`libgssapi-krb5-2`**, because Npgsql logs `Error: libgssapi_krb5.so.2`
    at start-up without it. The image is 278 MB.
  - `.dockerignore` keeps bin/obj, node_modules, wwwroot, tests, openspec, docs and local out of the
    build context.
- **Checked against compose** (network `panopticon_default`): `--migrate` reported "No migrations were
  applied". The server listens on `[::]:8080` as `uid=1654(app)`, with a clean log.
  - `/` returned 200 with title "a PANOPTICON". The real `/assets/index-*.js` came back as
    `text/javascript`, the CSS as `text/css`, and the `.woff2` fonts as `font/woff2`.
  - `/api/changes` returned 200, and `/healthz` 200.
  - Reporting through the published bridge with the right token gave `Overall 27/30 · section 8 Container
    and end-to-end check 0/3`. A wrong token gave a "rejected the token (401)" tool error.
  - `GET /mcp` returns 404, because the stateless endpoint only maps POST. That's expected.
- **8.2 `README.md`:** what it is, prerequisites (`make deps`), running locally (compose, `npm ci` + build,
  `ASPNETCORE_ENVIRONMENT=Development dotnet run`, port 7480, dev token), dashboard hot reload, stopping,
  a configuration table (every `Panopticon:*` key, plus `ConnectionStrings:Panopticon` and `Urls`),
  `--migrate`, the container commands with `--env-file local/container.env` (new, public compose values),
  and the gates. There's a "local use only" warning, per the spec.
  - **Fresh-clone check:** cloned HEAD into the scratchpad, plus the uncommitted §8 files, and followed
    the README word for word. Compose was healthy, the web build passed, and the Development server
    listened on 7480. `/` and the real JS asset loaded, and `/api/changes` listed the existing card.
  - Compose volumes are shared by project name, so the database wasn't empty. Not tested: the empty state
    on a truly fresh database.
  - Then: ran the README's container commands from the clone (worked), stopped the clone's server, ran
    `docker compose up -d` from the main repo (the s3 bind-mount is back on
    `panopticon/local/seaweedfs-s3.json`), and deleted the clone.
  - npm 11 printed an `install-scripts` notice, but the build is fine without the scripts.
- **Deferred:** DataProtection keys aren't persisted in the container (a warning). Nothing uses them
  until sign-in.

**Running now:** container `panopticon-check` from image `panopticon`, on **7480 → 8080**, with
`local/container.env` (token `dev-bootstrap-token`). The dev server is stopped. Clean up with
`docker rm -f panopticon-check`.

## Recipe

1. Emmz opens http://localhost:7480 (served by the container), with the `walking-skeleton` card at 27/30.
2. Emmz says "go". Claude ticks 8.1 and 8.2 in `tasks.md`, then calls `report_progress`
   (`walking-skeleton`, section 8) through the `panopticon` MCP server in this Claude Code session.
3. Expect the card at section 8 going 0/3 → 2/3, and the whole-change bar going 27/30 → 29/30, both
   lighting their new cells, with no reload.

## Next

- **On PASS:** tick 8.3, run `make gates`, and write the §8 section-done note. Commit
  `feat(walking-skeleton): container and end-to-end check (§8)`. Then **Finishing a change**:
  whole-change self-review from `7d3feb6^`, the change-done note, and an offer of `code-review`. Also check
  `tasks.md` "## Workflow follow-up" first.
- **On FAIL:** fix within §8, and re-check.
