# a PANOPTICON

A live dashboard of coding agents working through [OpenSpec](https://github.com/Fission-AI/OpenSpec) changes.
Agents report through a small local MCP bridge (the `panopticon` CLI). The server parses each change's
`tasks.md`, and the dashboard shows one card per change, with section and whole-change progress updating live.
Each card opens the change's own page, which shows the documents in its folder. Once a change is archived with
`/opsx:archive`, it leaves the dashboard for the history.

- Requirements: [`docs/DISCOVERY.md`](docs/DISCOVERY.md). Decisions: [`docs/adrs/`](docs/adrs/).
- Connecting an agent: [`docs/cli.md`](docs/cli.md).
- Dashboard design: [`web/DESIGN.md`](web/DESIGN.md).

Members sign in to the dashboard with a link sent by email, and each machine that reports gets its own API token
through `panopticon login`.

> **Keep a Development server to this machine.** In Development the server logs sign-in links instead of emailing
> them, and it accepts a public bootstrap token for reports. Anyone who can reach it could use either.

## Prerequisites

The tools are listed in [`docs/dependencies.md`](docs/dependencies.md): the .NET 10 SDK, Node 22 or later,
Docker (OrbStack or Docker Desktop) and git. `make deps` checks them all and prints `DEPS_EXIT:0` when they're
there.

## Run it locally

From the repo root:

```sh
docker compose up -d --wait          # Postgres 17 and an S3-compatible store (SeaweedFS), with the bucket created
npm --prefix web ci                  # once, and after web dependency changes
npm --prefix web run build           # builds the dashboard into src/Panopticon.Server/wwwroot
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

Then open <http://localhost:7480> and sign in (below). With no reports yet, the dashboard explains how to connect an
agent.

In Development, the server applies database migrations at start-up. It reads its settings from
[`src/Panopticon.Server/appsettings.Development.json`](src/Panopticon.Server/appsettings.Development.json).
Those settings match `compose.yaml`, the initial member is `dev@localhost.test`, and the bootstrap token is
`dev-bootstrap-token`. They're public, local-only values.

### The first sign-in

Members come from `Panopticon:InitialMembers` at first, and anyone signed in can invite more from the Members page.

1. On the sign-in page, enter an initial member's address (`dev@localhost.test` locally), and choose **Send me a
   link**.
2. Open the link. With no Resend key (Development), there's no email. The server logs the message as a warning
   instead, starting `Development: not sending email to dev@localhost.test`, and the link ends in
   `/sign-in/confirm#…`. Otherwise it's in your inbox, and it works once, for 15 minutes.
3. Choose **Sign in**. Opening the link alone doesn't sign you in, so a mail scanner that follows it can't.

Rename yourself on the Members page: the name appears on the cards next to every agent you run.

### Reporting progress

Build and install the CLI with `make install` (it puts `panopticon` in `~/.local/bin`; set `INSTALL_DIR` for
elsewhere), then give this machine a token:

```sh
panopticon login --server http://localhost:7480
```

It prints a code and a link. Open the link while signed in, check the code, and choose **Approve**. The CLI saves
the token, and `panopticon mcp` uses it from then on. Connecting Claude Code and other agents is covered in
[`docs/cli.md`](docs/cli.md). The Tokens page lists every machine's token, and revoking one stops its reports at once.

### Change pages and the history

Each report sends the whole change folder: `proposal.md`, `design.md`, `tasks.md`, the specs, the devlog and any
other files. Choose a change's name on its card to open its page. The page lists those documents, grouped, and
shows one at a time, rendered from Markdown. It opens on the proposal, and it updates live while the change is
active.

When `/opsx:archive` moves the folder to `openspec/changes/archive/`, a running `panopticon mcp` that reported the
change notices, and reports it as archived. The card leaves the dashboard, and the change appears on the History
page, newest first, with its documents as they were when it was archived. If no bridge was running, the next report
of that change name archives it instead. How the bridge finds archives is in [`docs/cli.md`](docs/cli.md#archives).

**After deploying this version**, changes archived before it are still cards. Report each one once from its repo with
the new CLI (ask the agent to report it, or call `report_progress` with its name), and it moves to the history.

### Working on the dashboard

`npm --prefix web run dev` serves the dashboard with hot reload, at the URL Vite prints. It proxies `/api` and
`/mcp` to the server on 7480 (override with `PANOPTICON_DEV_SERVER`). Keep the server running alongside it.

### Stopping

Stop the server with Ctrl+C. `docker compose down` stops the services and keeps their data, and
`docker compose down -v` deletes it too.

## Configuration

These settings come from `appsettings*.json` or from environment variables. In an environment variable, `:`
becomes `__`, e.g. `Panopticon__S3__Bucket`.

| Setting | Meaning |
| --- | --- |
| `ConnectionStrings:Panopticon` | The Postgres connection string. |
| `Panopticon:InitialMembers` | The first members' email addresses, as a list or one comma-separated value. Each one is added at start-up unless it already has a member record, and the server won't start if there's no active member. |
| `Panopticon:PublicUrl` | The server's public base URL. Sign-in links and `panopticon login`'s approval link are built on it, never on the request's host. Required outside Development, which uses `http://localhost:7480`. |
| `Panopticon:Resend:ApiKey` | The [Resend](https://resend.com) API key for sign-in emails. Required outside Development. In Development, leaving it unset logs each email instead of sending it. |
| `Panopticon:Email:From` | The sender of sign-in emails, e.g. `a PANOPTICON <sign-in@example.com>`, on a domain verified in Resend. Required whenever a Resend key is set. |
| `Panopticon:BootstrapToken` | A shared bearer token agents can report with, **in Development only**. The server won't start with it set in any other environment. Use `panopticon login` instead. |
| `Panopticon:Activity:IdleAfter`, `Panopticon:Activity:StaleAfter` | How long after its last report a dashboard card shows as idle, then as stale, as a TimeSpan (e.g. `00:30:00`). The defaults are 30 minutes and 4 hours. Both must be positive, and the server won't start unless `StaleAfter` is longer than `IdleAfter`. |
| `Panopticon:S3:ServiceUrl` | The S3 endpoint for change documents. Leave it unset for AWS itself. |
| `Panopticon:S3:Region` | The S3 region. The default is `us-east-1`. |
| `Panopticon:S3:AccessKey`, `Panopticon:S3:SecretKey` | The S3 credentials. |
| `Panopticon:S3:Bucket` | The bucket for change documents. It must already exist. |
| `Panopticon:S3:ForcePathStyle` | `true` for path-style S3 URLs (SeaweedFS, MinIO). |
| `Urls` | The address the server listens on. Development uses `http://localhost:7480`, and the container uses port 8080. |

### Migrations

Outside Development, migrations don't run by themselves. Run the server once with `--migrate`: it applies any
pending migrations, then exits. Do that before starting a new version, with the same settings (environment variables)
as the server.

```sh
dotnet run --project src/Panopticon.Server -- --migrate
```

## Run it in a container

The `Dockerfile` builds the dashboard and the server into one image. To run it against the compose services:

```sh
docker build -t panopticon .

docker run --rm --network panopticon_default --env-file local/container.env panopticon --migrate
docker run --rm --network panopticon_default --env-file local/container.env -p 127.0.0.1:7481:8080 panopticon
```

Then open <http://localhost:7481> and sign in as `dev@localhost.test`. The link appears in the container's output
(or `docker logs`). Publishing on `127.0.0.1` keeps the dashboard to this machine, which matters because the
container runs as Development. `compose.yaml` binds its ports the same way.
`local/container.env` holds the compose values, with the service names as hosts. It runs the container as Development (a
local run: sign-in links are logged, and the bootstrap token works):

```sh
ASPNETCORE_ENVIRONMENT=Development
Urls=http://+:8080
ConnectionStrings__Panopticon=Host=postgres;Port=5432;Database=panopticon;Username=panopticon;Password=panopticon
Panopticon__PublicUrl=http://localhost:7481
Panopticon__InitialMembers=dev@localhost.test
Panopticon__S3__ServiceUrl=http://s3:8333
Panopticon__S3__AccessKey=panopticon-local
Panopticon__S3__SecretKey=panopticon-local-secret
Panopticon__S3__Bucket=panopticon-documents
Panopticon__S3__ForcePathStyle=true
```

## Development

Builds are gated through the `Makefile`. `make gates` runs everything (build, tests, format, the CLI's Native
AOT publish, the web build/test/format/lint, and OpenSpec validation). `make test` needs Docker
(Testcontainers).

`make e2e` isn't a gate. It builds this image, then runs the whole team flow against it: it signs in through the
logged link, runs `panopticon login` and approves it, and reports a whole change folder through `panopticon mcp`. It
checks that the card names the member and that the change page lists the documents. Then it archives the folder and
waits for the running bridge to move the change to the history. It needs Docker, and the first image build takes a
few minutes.

The workflow for building changes is in [`CLAUDE.md`](CLAUDE.md).
