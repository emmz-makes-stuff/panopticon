# Proposal

## Why

Every machine that reports needs the `panopticon` CLI. Today the only way to get it is `make install`, which needs
a clone of the repo and the .NET SDK, and a fresh cloud VM has neither. ADR-0004 planned to publish binaries as GitHub
Release assets, but the repo is private, so they couldn't be downloaded anonymously. The server is the one public thing
every user can already reach, so it should hand out the CLI.

## What Changes

- **Install scripts served by the server.** `curl -fsSL https://<server>/install.sh | sh` (Linux, macOS) and
  `irm https://<server>/install.ps1 | iex` (Windows). The script works out the platform, downloads that build from the
  same server, checks its SHA-256, installs it to a user directory, and prints the `panopticon login --server <server>`
  command to run next. The server builds every URL in the script from `Panopticon:PublicUrl`, so self-hosted servers
  hand out their own CLI.
- **The server serves the CLI binaries** for linux-x64, linux-arm64, osx-arm64 and win-x64, plus a version manifest
  (version, file names, SHA-256s). These don't need sign-in. The CLI is useless without an approved
  `panopticon login`.
- **The image bundles the binaries.** The release workflow builds them, and the server image copies them in. An image
  built without them (local `docker build`, `make e2e` without a prepared CLI) still runs. It says there are no CLI
  builds, rather than failing.
- **One version for server and CLI.** The `v*` tag workflow gains a per-OS Native AOT build matrix (AOT can't build
  for another OS). It stamps the tag's version into the CLI, attaches the binaries and checksums to a GitHub Release
  for the record, and passes them into the Railway image build.
- **`panopticon update`** fetches the build that the server it's logged in to serves, checks its SHA-256 and
  replaces the running binary. It does nothing if the versions already match. **`panopticon --version`** prints the
  CLI's version.
- **An install hint in the web UI.** The Tokens page shows the one-line install command for the visitor's OS, with
  the other platforms one click away. If the server has no CLI builds, it says so instead.
- **ADR-0004 is amended.** The server becomes the distribution channel. GitHub Releases stay as the record.
- `make install` stays as the way to install from source for local development.

Out of scope: a Homebrew tap, winget and Scoop, osx-x64 builds, code signing and notarisation, and automatic update
checks.

## Capabilities

### New Capabilities

- `cli-distribution`: how a machine gets and updates the CLI. It covers the server's install scripts, binaries and
  version manifest, the release build that makes them, `panopticon update` and `--version`, and the install hint on
  the Tokens page.

### Modified Capabilities

(none: `cli-bridge`'s "Single self-contained binary" requirement stays as it is, and this change only distributes
that binary)

## Impact

- **Server** (`src/Panopticon.Server`): new anonymous endpoints `/install.sh`, `/install.ps1`, `/cli/{rid}/{file}` and
  `/api/cli`, plus a setting for the CLI directory, which defaults to the bundled path in the image. The fallback route
  must leave these paths alone.
- **CLI** (`src/Panopticon.Cli`): `update` and `--version` commands, and replacing its own binary on each OS. On
  Windows, it renames the running `.exe` aside.
- **Web** (`web/src/views/tokens.ts`): an install-command panel.
- **Build and release**: `Dockerfile` (copies the bundled binaries in), `.github/workflows/deploy.yml` (build matrix,
  GitHub Release, deploy uses the binaries), and possibly `.railwayignore`/`.dockerignore`, so `railway up` uploads
  the binaries. The workflow needs `contents: write`, and an arm64 Linux runner.
- **Docs**: `docs/cli.md` (Install, Update), `README.md`, ADR-0004 amendment.
- **Dependencies**: none new on Emmz's machine. CI uses GitHub-hosted runners for each OS.
