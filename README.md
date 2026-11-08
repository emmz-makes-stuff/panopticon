# a PANOPTICON

A live dashboard of coding agents working through [OpenSpec](https://github.com/Fission-AI/OpenSpec) changes.
Agents report through a small local MCP bridge (the `panopticon` CLI). The server parses each change's
`tasks.md`, and the dashboard shows one card per change, with section and whole-change progress updating live.

- Requirements: [`docs/DISCOVERY.md`](docs/DISCOVERY.md). Decisions: [`docs/adrs/`](docs/adrs/).
- Connecting an agent: [`docs/cli.md`](docs/cli.md).
- Dashboard design: [`web/DESIGN.md`](web/DESIGN.md).

> **Local use only for now.** The dashboard and its read API don't need a sign-in yet, and reports use a single
> bootstrap token. Don't expose a server to the internet until sign-in lands.

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

Then open <http://localhost:7480>. With no reports yet, the dashboard explains how to connect an agent.

In Development, the server applies database migrations at start-up. It reads its settings from
[`src/Panopticon.Server/appsettings.Development.json`](src/Panopticon.Server/appsettings.Development.json).
Those settings match `compose.yaml`, and the bootstrap token is `dev-bootstrap-token`. They're public, local-only
values.

To report progress, build the CLI with `make aot` and follow [`docs/cli.md`](docs/cli.md).

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
| `Panopticon:PublicUrl` | The server's public base URL, used for the links it sends. Development uses `http://localhost:7480`. |
| `Panopticon:BootstrapToken` | A shared bearer token agents can report with, **in Development only**. The server won't start with it set in any other environment. |
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

Then open <http://localhost:7481>. Publishing on `127.0.0.1` keeps the dashboard to this machine, which matters
while it has no sign-in and the token is public. `compose.yaml` binds its ports the same way.
`local/container.env` holds the compose values, with the service names as hosts:

```sh
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
(Testcontainers). The workflow for building changes is in [`CLAUDE.md`](CLAUDE.md).
