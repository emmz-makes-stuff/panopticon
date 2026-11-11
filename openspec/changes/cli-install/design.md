# Design

## Context

- The CLI is a Native AOT console app (`src/Panopticon.Cli`). `make aot` publishes it for the current machine only.
  It already reads `AssemblyInformationalVersionAttribute` to describe itself to MCP clients
  (`Bridge/BridgeHost.cs`), but no version is stamped, so every build is `1.0.0`.
- The server image (`Dockerfile`) has three stages: web, then server publish, then the `aspnet` runtime. The
  `.dockerignore` keeps `artifacts/` out of the build context.
- `.github/workflows/deploy.yml` runs `railway up` on `v*` tags. `railway up` uploads the working directory as the build
  context, and Railway builds the `Dockerfile` remotely. It skips files matched by `.gitignore`.
- The server maps endpoints in `ServerSetup.MapPanopticon`. There, `UseStaticFiles` runs first. Last comes a fallback
  to `index.html` for any path without a file extension outside `api/` and `mcp/`. `PublicUrlOptions` holds
  `Panopticon:PublicUrl`, which every outward link is built on.
- Native AOT can't build for another OS. Each platform's binary needs a runner on that OS.

## Goals / Non-Goals

**Goals:**
- Every release artefact is built once, in CI, and its checksum travels with it to the Release, the image, the
  manifest and the install scripts.
- The install scripts need nothing but `sh` with `curl` or `wget`, and `sha256sum` or `shasum`. Or, on Windows,
  PowerShell 5.1+. No `jq`, and no JSON parsing in `sh`.
- Local development is unchanged: `make install`, `make test` and `docker build` work without any CLI builds.

**Non-Goals:**
- Version ordering (semver comparison). The CLI follows its server, so equality is the only test.
- Signing, notarisation, a Homebrew tap, winget and Scoop, musl/Alpine and osx-x64 builds.
- Update checks inside `panopticon mcp`.

## Decisions

### D1. The image carries a `cli/` directory, and the server reads it at start-up

The image's `/app/cli/` directory holds `<rid>/<file>` for each platform, plus a `version` file. At start-up, the server
scans `Panopticon:CliDirectory` (default `<app base>/cli`), hashes each file (SHA-256) and keeps an immutable catalogue
in memory. A missing directory, or one with no `version` file, gives an empty catalogue: "no CLI builds". It doesn't
fail start-up.

- *Why:* the server computes the manifest from the very bytes it serves, so the checksums can't drift. Four
  files of about 10–20 MB hash in well under a second.
- *Alternative:* a `manifest.json` that CI writes. That means a second source of truth to keep consistent, and a
  hand-edited build could lie about it.
- *Alternative:* the S3 bucket. CI would need bucket credentials, and the image would no longer be a whole release
  on its own.

### D2. Build context: a tracked `cli-dist/` placeholder, and a staged context in CI

`cli-dist/` is the build-context directory the `Dockerfile` copies to `/app/cli`
(`COPY cli-dist/ /app/cli/`). Only `cli-dist/.keep` is tracked. `.gitignore` has `cli-dist/*` and `!cli-dist/.keep`,
so `COPY` always works and local images have no CLI builds. Because `railway up` skips gitignored files, the deploy
job doesn't upload the checkout. It stages a context instead:

1. `git archive HEAD` goes into `$RUNNER_TEMP/context`.
2. The downloaded binaries and `version` go into its `cli-dist/`.
3. The two `cli-dist` lines come out of the staged `.gitignore`.
4. `railway up "$RUNNER_TEMP/context" --path-as-root` runs.

- *Why:* the context holds exactly what was tagged, plus the binaries. It doesn't rely on how `.railwayignore`
  negation interacts with `.gitignore`, which isn't clearly documented.
- *Alternative:* build the image in Actions and push it to a registry, then point Railway at the image. That changes
  how ADR-0004 deploys and needs registry credentials. Overkill.
- §6 confirms `--path-as-root` on Railway CLI 5.71. If it doesn't behave, the fallback is `cd` into the staged
  directory and run `railway up` there.

### D3. Install scripts are templates rendered per request

`install.sh` and `install.ps1` are embedded resources with placeholders. The server fills them in from the catalogue
and `PublicUrl`: the version, the public URL, and each platform's URL and SHA-256. Both are served as
`text/plain; charset=utf-8`, with `Cache-Control: no-cache`. The `sh` script picks a platform from `uname -s`/`uname
-m`. It rejects musl when `ldd --version` mentions musl or `/lib/ld-musl-*` exists. It downloads to a temp file in the
target directory, checks the SHA-256, then `chmod 755` and `mv` it over the old one.

- *Why:* the scripts need no JSON parsing and no second request, and a script can only ever install its own
  server's release.
- *Alternative:* static scripts that fetch `/api/cli`. That needs `jq` or fragile `sed` on the VM.

### D4. Routes

All of these are anonymous, and all are mapped before the fallback:

- `GET /api/cli`
- `GET /install.sh`, `GET /install.ps1`
- `GET /cli/{rid}/{file}`: 404 unless `{rid}` is in the catalogue and `{file}` is its file name.
  `application/octet-stream`, with `Content-Disposition: attachment`.

An explicit endpoint already outranks the fallback, so `/cli/...` never returns `index.html`. A test pins this down.
With an empty catalogue, all four return 404 with a plain-text message (JSON `{ "error": … }` for `/api/cli`).

The `/api/cli` shape:

```json
{ "version": "0.2.0",
  "installSh": "https://panopticon.team/install.sh",
  "installPs1": "https://panopticon.team/install.ps1",
  "platforms": { "linux-x64": { "url": "…/cli/linux-x64/panopticon", "size": 12345, "sha256": "…" }, … } }
```

The JSON contract goes in `Panopticon.Contracts`, so the CLI's `update` and the server share one type (source-generated
JSON for AOT).

### D5. Versioning

The tag `vX.Y.Z` gives `X.Y.Z`. CI publishes with `-p:Version=X.Y.Z` and writes `X.Y.Z` to `cli-dist/version`. The CLI
project sets a default `<Version>0.0.0-dev</Version>`, so a local build never looks like a release.
`panopticon --version` prints the informational version up to any `+<commit>` suffix. The server's own assembly
version isn't stamped in this change, because the CLI catalogue's `version` is what clients compare.

### D6. Self-update replaces the running binary in place

`update` resolves the server the same way `login` does (`--server`, then `PANOPTICON_SERVER_URL`, then the saved
login). It gets `/api/cli`, picks `RuntimeInformation.RuntimeIdentifier`'s entry, and does nothing if the versions are
equal. Otherwise it downloads to `<dir of Environment.ProcessPath>/.panopticon-update-<random>`, checks the SHA-256 and
the size, sets mode 755 on Unix, then:

- **Unix:** `File.Move(temp, processPath, overwrite: true)`. A rename is atomic, and the running process keeps its
  inode.
- **Windows:** a running `.exe` can't be overwritten, but it can be renamed. So `processPath` is renamed to
  `panopticon.exe.old`, and then the temp file is moved to `processPath`. If that second step fails, the rename is
  undone. The next start of any `panopticon` command deletes a stale `.old` file, best effort and silent.

Any failure deletes the temp file and leaves the old binary in place (spec: Safe self-update). The HTTP client and the
file-system steps sit behind small seams, so unit tests can cover the mismatch, no-build, unwritable and Windows-path
cases without a network.

### D7. Release workflow

`deploy.yml` becomes three jobs on `v*`:

1. **`cli`** runs a matrix:

   | Runner             | Platform    |
   | ------------------ | ----------- |
   | `ubuntu-22.04`     | linux-x64   |
   | `ubuntu-22.04-arm` | linux-arm64 |
   | `macos-15`         | osx-arm64   |
   | `windows-2025`     | win-x64     |

   Each runner sets up .NET from `global.json`, publishes with the version, and uploads `cli-<rid>` as an artefact.
   Linux builds on 22.04 so the binary needs only glibc ≥ 2.35. That covers Ubuntu 22.04+ and Debian 12+.
2. **`release`** (needs `cli`, `contents: write`) downloads all four and writes `SHA256SUMS`. It then runs
   `gh release create "$tag"` with the assets renamed `panopticon-<rid>[.exe]`, and generates notes.
3. **`deploy`** (needs `cli` and `release`) stages the context (D2) and runs the existing Railway steps.

The matrix has `fail-fast: true`, and both later jobs need it, so one failed build means no Release and no deploy.

### D8. Install hint on the Tokens page

`tokens.ts` fetches `/api/cli` alongside the token list. It picks the default tab from
`navigator.userAgentData?.platform ?? navigator.platform`: `Win…` gives PowerShell, anything else gives `sh`. Two
tabs show the command in a `<code>` block with a Copy button (`navigator.clipboard.writeText`). A 404 shows the "no
CLI builds — `make install`" note. The visual design goes through the `frontend-design` pass and Emmz's review, as for
other dashboard UI.

### D9. Tests

- **Server integration tests:** these use a temp `CliDirectory` with fake files. They cover the manifest and its
  hashes, `PublicUrl` vs `Host`, downloads, unknown rid/file returning 404 rather than `index.html`, the empty
  catalogue, and rendered scripts holding the right URLs and hashes.
- **Install-script tests:** these run the rendered `install.sh` with `sh` against a Kestrel test host serving a fake
  "binary" (a tiny `sh` script that prints a version). They cover install, `PANOPTICON_INSTALL_DIR`, the `PATH` hint,
  checksum mismatch leaving the old file, and an unsupported platform (simulated through a `PANOPTICON_UNAME_S`/`_M`
  override that only the tests use). These skip on Windows.
- **CLI unit tests:** these cover `update` (equal, newer, older, 404, no platform, mismatch, unwritable, the Windows
  rename path) and `--version`.
- **`make e2e`:** this gains a step. It builds the Linux CLI for the container's arch inside the SDK image into
  `cli-dist/`, builds the server image with it, then runs `curl …/install.sh | sh` in a clean `debian:bookworm-slim`
  container and checks `panopticon --version`.
- `install.ps1` has no automated test (no `pwsh` in `docs/dependencies.md`). It's a human-verification stop.

## Risks / Trade-offs

- [GitHub-hosted arm64 and macOS runners on a private repo cost more minutes (macOS is billed at 10×)] → one run
  per tag only. The first tagged run confirms `ubuntu-22.04-arm` is available to this repo. If it isn't, Linux arm64
  falls back to an x64 runner using the .NET cross-build container.
- [glibc floor: built on 22.04 means old distros such as Debian 11 can't run it] → document the floor in
  `docs/cli.md`. The script can't cheaply detect the glibc version, so it fails at the `--version` check it runs after
  installing, and prints the error.
- [Unsigned macOS binary] → `curl` doesn't set the quarantine attribute, and the AOT linker ad-hoc signs arm64
  binaries, so Gatekeeper doesn't block it. A browser download would be quarantined, so the docs point at the script.
- [Anonymous downloads expose the compiled CLI to anyone with the URL] → accepted by Emmz. It holds no secrets and
  needs an approved login to do anything.
- [Image size grows by roughly 50–80 MB] → acceptable for one container. The binaries sit in their own layer.
- [Self-update replaces the binary while a `panopticon mcp` bridge is running] → the running bridge keeps its old
  inode or `.old` file. `update` prints "restart your agent to use the new version".

## Migration Plan

- The first tag after this lands (`v0.2.0`) publishes the first Release and the first served builds. Machines that
  installed with `make install` run `curl …/install.sh | sh` once, or keep using `make install`.
- **Rollback:** re-deploying an older tag serves its builds, and `panopticon update` follows the server down (spec:
  "Server has an older build").
- ADR-0004 gets an *Amended 2026-11-11* bullet: the server serves the binaries, and GitHub Releases are kept as
  the record.
