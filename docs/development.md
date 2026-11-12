# Development

Running Panopticon from a checkout, working on it, and the gates every change has to pass. To run a server for real,
see [`self-hosting.md`](self-hosting.md).

> **Keep a Development server to this machine.** In Development the server logs sign-in links instead of emailing
> them, and it accepts a public bootstrap token for reports. Anyone who can reach it could use either.

## Prerequisites

The tools are listed in [`docs/dependencies.md`](dependencies.md): the .NET 10 SDK, Node 22 or later,
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

Then open <http://localhost:7480> and sign in as `dev@localhost.test` (see
[The first sign-in](using.md#the-first-sign-in)). With no reports yet, the dashboard explains how to connect an agent.
To report from this machine, a local server has no CLI builds to hand out, so install the CLI from source with
`make install` (see [Reporting progress](using.md#reporting-progress)).

In Development, the server applies database migrations at start-up. It reads its settings from
[`src/Panopticon.Server/appsettings.Development.json`](../src/Panopticon.Server/appsettings.Development.json).
Those settings match `compose.yaml`, the initial member is `dev@localhost.test`, and the bootstrap token is
`dev-bootstrap-token`. They're public, local-only values.

## Working on the dashboard

`npm --prefix web run dev` serves the dashboard with hot reload, at the URL Vite prints. It proxies `/api` and
`/mcp` to the server on 7480 (override with `PANOPTICON_DEV_SERVER`). Keep the server running alongside it.

## Stopping

Stop the server with Ctrl+C. `docker compose down` stops the services and keeps their data, and
`docker compose down -v` deletes it too.

## Gates

Builds are gated through the `Makefile`. `make gates` runs everything (build, tests, format, the CLI's Native
AOT publish, the web build/test/format/lint, and OpenSpec validation). `make test` needs Docker
(Testcontainers).

`make e2e` isn't a gate. It builds the server image from the `Dockerfile`, then runs the whole team flow against it: it signs in through the
logged link, runs `panopticon login` and approves it, and reports a whole change folder through `panopticon mcp`. It
checks that the card names the member and that the change page lists the documents. Then it archives the folder and
waits for the running bridge to move the change to the history. It needs Docker, and the first image build takes a
few minutes.

Before contributing, read [`CONTRIBUTING.md`](../CONTRIBUTING.md) (and, for coding agents,
[`AGENTS.md`](../AGENTS.md)). The maintainer's own workflow for building changes is in [`CLAUDE.md`](../CLAUDE.md).
