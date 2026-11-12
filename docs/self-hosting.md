# Hosting your own server

A Panopticon server is one container, a Postgres database and an S3-compatible bucket. This guide takes you from a
checkout to a server that signs your first member in by email and accepts `panopticon login` from your members'
machines. Then it covers upgrading and backups.

The examples use `panopticon.example.com`. Put your own domain in its place.

## Before you start

> **Never run a real server as Development.** In Development the server logs sign-in links instead of emailing them,
> and it accepts a public bootstrap token for reports. Anyone who can reach it could use either. The container runs
> as Production unless you set `ASPNETCORE_ENVIRONMENT`, so leave it unset. Development is for a server on your own
> machine ([`development.md`](development.md)).

You need:

- **A container host** that can build or pull an image and run it with environment variables. Any Docker host will
  do. [Railway](#deploy-to-railway) is the reference deployment.
- **Postgres.** The repo's `compose.yaml` and its tests use Postgres 17.
- **An S3-compatible bucket**, which must already exist, with an access key that can read and write it. AWS S3, a
  Railway Bucket, MinIO, SeaweedFS and the like all work. Change documents (each change's whole folder, as reported)
  live here, not in Postgres.
- **A [Resend](https://resend.com) account**, with an API key and a sending domain verified in Resend. Sign-in links
  and invitations go out through it.
- **A public HTTPS address** for the server, such as `https://panopticon.example.com`, through a proxy or platform
  that terminates TLS and forwards to the container's port 8080. Sign-in links and `panopticon login` approval links
  are built on this address, and `panopticon update` only updates over https.

## Build the image

The `Dockerfile` builds the dashboard and the server into one image. Build it from the repo root, at the release tag
you want to run:

```sh
git checkout v0.3.0                 # or the latest v* tag
docker build -t panopticon .
```

The image also carries the CLI builds that the server hands out to your members, copied from `cli-dist/`. In a plain
checkout that folder is empty, so read [Give your members the CLI](#give-your-members-the-cli) before you build an
image to deploy.

There's no published image. Build your own, and push it to whatever registry your host pulls from.

## Configure

These settings come from `appsettings*.json` or from environment variables. In an environment variable, `:`
becomes `__`, e.g. `Panopticon__S3__Bucket`. Only `appsettings.Development.json` sets any of them, so a production
server gets all of them from its environment.

| Setting | Required | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Panopticon` | Yes | The Postgres connection string. |
| `Panopticon:InitialMembers` | Yes | The first members' email addresses, as a list or one comma-separated value. Each one is added at start-up unless it already has a member record, and the server won't start if there's no active member. |
| `Panopticon:PublicUrl` | Yes | The server's public base URL. Sign-in links and `panopticon login`'s approval link are built on it, never on the request's host. Required outside Development, which uses `http://localhost:7480`. |
| `Panopticon:Resend:ApiKey` | Yes | The [Resend](https://resend.com) API key for sign-in emails. Required outside Development. In Development, leaving it unset logs each email instead of sending it. |
| `Panopticon:Email:From` | Yes | The sender of sign-in emails, e.g. `PANOPTICON <sign-in@example.com>`, on a domain verified in Resend. Required whenever a Resend key is set. |
| `Panopticon:S3:Bucket` | Yes | The bucket for change documents. It must already exist. |
| `Panopticon:S3:AccessKey`, `Panopticon:S3:SecretKey` | Yes | The S3 credentials. |
| `Panopticon:S3:ServiceUrl` | For anything but AWS | The S3 endpoint for change documents. Leave it unset for AWS itself. |
| `Panopticon:S3:Region` | No | The S3 region. The default is `us-east-1`. |
| `Panopticon:S3:ForcePathStyle` | No | `true` for path-style S3 URLs (SeaweedFS, MinIO). The default is `false`. |
| `Panopticon:Activity:IdleAfter`, `Panopticon:Activity:StaleAfter` | No | How long after its last report a dashboard card shows as idle, then as stale, as a TimeSpan (e.g. `00:30:00`). The defaults are 30 minutes and 4 hours. Both must be positive, and the server won't start unless `StaleAfter` is longer than `IdleAfter`. |
| `Panopticon:Prompts:HoldLimit` | No | How long a question or permission prompt an agent holds for the dashboard waits for an answer before going back to the terminal. The default is `08:00:00`. |
| `Panopticon:Prompts:WaitGrace`, `Panopticon:Prompts:WaitTimeout`, `Panopticon:Prompts:LateEventWindow` | No | Timings for the agent hooks behind held prompts: how long a held prompt survives with no hook waiting on it (`00:01:30`), how long one wait lasts (`00:00:25`), and how long a session's end turns away its own late events (`00:01:00`). Leave them alone unless you know why. |
| `Dashboard:KeepAlive` | No | How often an open dashboard's live-update stream sends a keep-alive when nothing else is happening, as a TimeSpan. The default is `00:00:25`. Lower it if your proxy closes idle connections sooner. It must be positive. |
| `Panopticon:CliDirectory` | No | Where the server finds the CLI builds it hands out. The default is the `cli` folder next to the server, which is where the image puts `cli-dist/`. |
| `Panopticon:BootstrapToken` | Never | A shared bearer token agents can report with, **in Development only**. The server won't start with it set in any other environment. Use `panopticon login` instead. |
| `Urls` | No | The address the server listens on. Development uses `http://localhost:7480`, and the container uses port 8080. |

A production env file looks like this (keep it out of git):

```sh
ConnectionStrings__Panopticon=Host=db.example.com;Port=5432;Database=panopticon;Username=panopticon;Password=…
Panopticon__PublicUrl=https://panopticon.example.com
Panopticon__InitialMembers=you@example.com
Panopticon__Email__From=PANOPTICON <sign-in@example.com>
Panopticon__Resend__ApiKey=…
Panopticon__S3__ServiceUrl=https://s3.example.com
Panopticon__S3__AccessKey=…
Panopticon__S3__SecretKey=…
Panopticon__S3__Bucket=panopticon-documents
```

A setting that's missing or wrong stops the server at start-up with a message naming it, rather than failing later.

## Migrate

Outside Development, migrations don't run by themselves. Run the server once with `--migrate`: it applies any
pending migrations, then exits. Do that before starting a new version, with the same settings (environment variables)
as the server.

```sh
docker run --rm --env-file panopticon.env panopticon --migrate
```

From a checkout, without the image, it's `dotnet run --project src/Panopticon.Server -- --migrate`.

## Run

```sh
docker run -d --name panopticon --restart unless-stopped --env-file panopticon.env -p 127.0.0.1:8080:8080 panopticon
```

Then point your TLS-terminating proxy at port 8080. `GET /healthz` answers `ok` once the server is up, for your
platform's health check.

**Behind a proxy**, the server sees the proxy's address for every client. The sign-in page's per-address limits (20
link requests and 10 `panopticon login` starts an hour) then apply to everyone together, not to each client. The
per-email limit (5 an hour) is unaffected. That's rarely a problem for a team, but it's worth knowing if sign-ins
start being refused.

### Try the image locally first

You can run the image against the repo's local services before deploying it. To run it against the compose services:

```sh
docker compose up -d --wait
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

`make e2e` runs the whole team flow against this image automatically: sign-in, `panopticon login`, a report, and the
card and change page that result.

## The first member

1. Open your server's address, enter one of the `Panopticon:InitialMembers` addresses, and choose **Send me a link**.
2. Open the link from the email (it works once, for 15 minutes), and choose **Sign in**.
3. Rename yourself on the Members page, then invite people. An initial member can invite anyone to the server from
   the Members page, and any Project owner can invite people to their Project.

The rest of signing in, Projects, tags and reporting is in [`using.md`](using.md).

## Give your members the CLI

Members install the `panopticon` CLI from your server: the setup guide on the Tokens page shows
`curl -fsSL https://panopticon.example.com/install.sh | sh` (or the PowerShell version), and `panopticon update` keeps
it at your server's version. The server hands out whatever is in its CLI directory (`cli-dist/` in the image):

```text
cli-dist/
  version                    # one line, e.g. 0.3.0
  linux-x64/panopticon
  linux-arm64/panopticon
  osx-arm64/panopticon
  win-x64/panopticon.exe
```

**Without builds**, everything else works, but the install scripts and the setup guide say the server has no CLI
builds. Members then build the CLI from a checkout with `make install`, which needs the .NET 10 SDK and a clang toolchain
(see [`dependencies.md`](dependencies.md) and [`cli.md`](cli.md#install)).

There are three ways to fill it before `docker build`:

- **From the release you're running.** Each GitHub Release has `panopticon-<platform>` files (`.exe` for Windows) and
  `SHA256SUMS`. Download the four for your tag, check them against `SHA256SUMS`, and put each at
  `cli-dist/<platform>/panopticon` (`panopticon.exe` for `win-x64`), with the tag's version, without the `v`, in
  `cli-dist/version`.
- **From your own fork's releases**, the same way (see [Releasing from a fork](#releasing-from-a-fork)).
- **Linux only:** `make cli-dist-linux CLI_DIST_VERSION=0.3.0` builds the Linux CLI for the Docker host's CPU in a
  container, and writes that version to `cli-dist/version`. Give it your tag's version: without one it's
  `0.0.0-dev`. That covers Linux VMs and containers, but not macOS or Windows machines.

The `.gitignore` keeps `cli-dist/` out of git, apart from its `.keep` file.

## Connect agents

Members connect their agents exactly as for any server: `panopticon login --server https://panopticon.example.com`,
then the Claude Code plugin or an `.mcp.json` entry. The plugin starts the bridge with the saved login, so it works
with every server. See [`using.md`](using.md#reporting-progress) and [`cli.md`](cli.md).

## Deploy to Railway

The reference deployment is one Railway project in a single region: a service built from the `Dockerfile`, Railway
Postgres, and a Railway Bucket. [`.railway/railway.ts`](../.railway/railway.ts) (Railway Infrastructure as Code) holds
the service's build and deploy settings: it runs `--migrate` as the pre-deploy command, checks `/healthz`, and pins the
region. It owns only the `panopticon` service. Postgres, the Bucket and the variables are managed outside it.
`railway config plan` shows any drift, and `railway config apply` fixes it (run `npm --prefix .railway ci` first).

Set up a project once:

```sh
railway init                                     # or `railway link` to an existing project
railway add --database postgres                  # then put its volume in the service's region
railway bucket create panopticon-documents --region iad
railway add --service panopticon
railway domain --service panopticon --port 8080
```

Then set the service's variables. References such as `${{Postgres.PGHOST}}` keep the credentials in Railway:

```sh
ConnectionStrings__Panopticon=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}
Panopticon__S3__ServiceUrl=${{panopticon-documents.ENDPOINT}}
Panopticon__S3__Region=${{panopticon-documents.REGION}}
Panopticon__S3__AccessKey=${{panopticon-documents.ACCESS_KEY_ID}}
Panopticon__S3__SecretKey=${{panopticon-documents.SECRET_ACCESS_KEY}}
Panopticon__S3__Bucket=${{panopticon-documents.BUCKET}}
Panopticon__S3__ForcePathStyle=false
Panopticon__PublicUrl=https://panopticon.example.com
Panopticon__InitialMembers=you@example.com
Panopticon__Email__From=PANOPTICON <sign-in@example.com>
Panopticon__Resend__ApiKey=…                      # railway variable set --stdin Panopticon__Resend__ApiKey
PORT=8080
```

To deploy from a checkout by hand, run `railway up --service panopticon`. That server has no CLI builds unless
`cli-dist/` holds them. `make cli-dist-linux` builds the Linux one for the Docker host's CPU, and the same applies to
a local `docker build`.

## Releasing from a fork

A pushed version tag releases the CLI and deploys the server, at one version. On every `v*` tag,
[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) runs these jobs:

1. **The CLI builds:** a Native AOT build on each OS (linux-x64, linux-arm64, osx-arm64, win-x64), stamped with the
   tag's version and checked with `--version`.
2. **The GitHub Release** for the tag, holding those four builds and `SHA256SUMS`, kept as the record.
3. **The deploy:** `railway config apply`, then `railway up` from a staged copy of the tag that has the same builds in
   `cli-dist/` ([`.github/scripts/stage-deploy-context.sh`](../.github/scripts/stage-deploy-context.sh)), so the deployed
   server hands them out. It uses a Railway project token for the production environment, in the `RAILWAY_TOKEN`
   repository secret.

If any build fails, nothing is released or deployed. A plan that would delete something fails the deploy rather than
going ahead. The service isn't connected to GitHub, so pushes to `main` don't deploy.

```sh
git tag v0.2.0 && git push origin v0.2.0
```

**In a fork**, the first two jobs work as they are, on GitHub's runners, so your fork gets its own releases with CLI
builds. The deploy job needs your own Railway project, set up as [above](#deploy-to-railway), and a Railway project
token for its production environment in your fork's `RAILWAY_TOKEN` secret. Without them, that job fails after the
release is made. The release is still usable: fill `cli-dist/` from it ([above](#give-your-members-the-cli)) and
deploy the image your own way. GitHub doesn't run a fork's workflows until you enable Actions on the fork.

## Upgrading

For each new release:

1. Check out the new tag, fill `cli-dist/` with the same version's builds, and build the image.
2. Run it once with `--migrate`, with the server's settings ([Migrate](#migrate)). Railway's pre-deploy command does
   this for you.
3. Replace the running container with the new image.

Members' CLIs catch up with `panopticon update`, which follows the server's version.

Some releases do one-off work the first time they start:

**Archived changes still showing as cards.** After deploying the version that added the history, changes archived
before it are still cards. Report each one once from its repo with the new CLI (ask the agent to report it, or call
`report_progress` with its name), and it moves to the history.

**Filling in the metrics.** After deploying the version that added the metrics, the server fills in the progress of
reports accepted before it, once, in the background at start-up. It reads each one's stored `tasks.md`, a batch at a
time, while it carries on serving.
- **While it runs:** the metrics cover what it has filled in so far.
- **A missing `tasks.md`:** that report is left out of the metrics, and the server logs its event id.
- **A storage error reading one report's `tasks.md`:** it logs that report and skips it, carrying on with the rest,
  and the next start tries it again. Three errors in a row mean the store is down: it stops with a warning, and the
  next start carries on from there.
- **Afterwards:** nothing is read twice, so a restart once it's done does nothing.

## Backups

Back up both the Postgres database and the bucket. Postgres holds members, Projects, tokens and every report's
progress. The bucket holds the documents behind change pages and the history. A restore needs both, from about the
same moment.
