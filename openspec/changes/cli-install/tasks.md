# Tasks

## 1. CLI: a version, and `--version`

- [x] 1.1 Set `<Version>0.0.0-dev</Version>` in `Panopticon.Cli.csproj`, and add `panopticon --version` (also
  `version`). It prints the informational version up to any `+<commit>` suffix, and exits 0 (D5). Add it to the
  `--help` text. Verify with a `CliApp` test, and with `make aot` then
  `dotnet publish … -p:Version=0.2.0` → `artifacts/cli/panopticon --version` prints `0.2.0`. Then `make build` →
  `BUILD_EXIT:0`.

## 2. Server: the CLI catalogue, manifest and downloads

- [x] 2.1 Add the `/api/cli` manifest contract to `Panopticon.Contracts`, with source-generated JSON (D4). Add a CLI
  catalogue that scans `Panopticon:CliDirectory` (default `<app base>/cli`) at start-up. It reads `version` and each
  `<rid>/<file>` for the four supported platforms, and hashes them. A missing or incomplete directory gives an empty
  catalogue, and start-up doesn't fail (D1). Verify with unit tests: a full directory, a missing directory, no
  `version` file, an unexpected rid ignored.
- [x] 2.2 Map `GET /api/cli` and `GET /cli/{rid}/{file}` as anonymous endpoints, with URLs built on `PublicUrl` (D4).
  An unknown rid or file is a 404, and an empty catalogue is a 404 with the "no CLI builds" message. Verify with
  integration tests against a temp `CliDirectory`: the manifest's values and hashes, a different `Host` header, a
  download's bytes and headers, `/cli/freebsd-x64/panopticon` → 404 and not `index.html`, and an empty catalogue
  for both routes. Then `make test` → `TEST_EXIT:0`.

## 3. Server: install scripts

- [x] 3.1 Add `install.sh` as an embedded template, rendered per request with the version, `PublicUrl`, and each
  platform's URL and SHA-256 (D3). Serve it at `GET /install.sh`, anonymous, `text/plain`, `no-cache`. It handles
  platform detection (with a test-only `PANOPTICON_UNAME_S`/`_M` override), musl rejection, `curl` or `wget`,
  `sha256sum` or `shasum`, `PANOPTICON_INSTALL_DIR`, an atomic replace, the `PATH` hint, a `--version` check after
  installing, and printing `panopticon login --server <PublicUrl>`. Verify with tests that run the rendered script
  under `sh` against a test host serving a fake binary: a fresh install, a custom directory, the `PATH` hint, a
  checksum mismatch leaving the old file, an Intel Mac, a musl override, and a self-hosted `PublicUrl` in every URL.
  These skip on Windows. Then `make test` → `TEST_EXIT:0`.
- [x] 3.2 Add `install.ps1` the same way, at `GET /install.ps1` (D3). It rejects non-x64, installs to
  `%LOCALAPPDATA%\Programs\panopticon`, checks the SHA-256 with `Get-FileHash`, adds that directory to the user
  `PATH` when it's missing, and prints the login command. Verify with an integration test that the rendered script
  holds this server's URL and the `win-x64` hash, and that an empty catalogue gives 404. The real Windows run is
  in 7.3.

## 4. CLI: `panopticon update`

- [ ] 4.1 Add `panopticon update [--server <url>]` (D6). It resolves the server as `login` does, gets `/api/cli`,
  and compares versions for equality. It then downloads this RID's build next to `Environment.ProcessPath`, checks
  the SHA-256 and size, and replaces the binary: an atomic move on Unix, rename-aside on Windows with an undo on
  failure. It prints the old and new versions and "restart your agent". Every `panopticon` start deletes a stale
  `panopticon.exe.old`. Add it to `--help`. Verify with unit tests through the HTTP and file-system seams: already
  current, newer, older, server 404, no build for this platform, checksum mismatch, unwritable directory, no
  server known, and the Windows rename path including its undo. Then `make test` → `TEST_EXIT:0`.
- [ ] 4.2 Add a CLI end-to-end test that drives the published binary. Copy it to a temp directory, point it at a test
  server whose catalogue holds a different "build" (a copy of the binary with a different `version` file), run
  `update`, and check that the file was replaced and that a second run says it's up to date. Then `make test` →
  `TEST_EXIT:0`.

## 5. Web: install hint on the Tokens page

- [ ] 5.1 On the Tokens page, fetch `/api/cli`, and show an "Install the CLI" panel with `sh` and PowerShell tabs. The
  default tab comes from the visitor's OS, and each tab has a Copy button. A 404 shows the `make install` note
  instead (D8). Add the copy to `web/DESIGN.md`'s Tokens section. Verify with `tokens.test.ts`: the macOS and Windows
  defaults, switching tabs, Copy calling the clipboard with the exact command, and the no-builds state. Then
  `make web-test` → `WEB_TEST_EXIT:0`, plus `make web-lint` and `make web-format`.
- [ ] 5.2 Human verification (stop and ask Emmz): the `frontend-design` pass on the panel, then her review of it in a
  real browser on the local server with a prepared `cli-dist`. Verify with Emmz's PASS, recorded in a devlog note.

## 6. Build and release

- [ ] 6.1 Add `cli-dist/.keep`, `.gitignore` entries `cli-dist/*` and `!cli-dist/.keep`, and
  `COPY cli-dist/ /app/cli/` in the `Dockerfile`'s runtime stage, in its own layer (D2). Add a
  `make cli-dist-linux` target that runs a Native AOT publish inside the SDK image (adding clang and zlib) for the
  Docker host's Linux arch, into `cli-dist/<rid>/`, and writes `cli-dist/version`. Verify that `docker build` works
  with and without it, and that the image built with it serves `/api/cli`.
- [ ] 6.2 Extend `make e2e`. It runs `make cli-dist-linux`, builds the image, then in a clean `debian:bookworm-slim`
  container runs `curl -fsSL <server>/install.sh | sh` and checks `panopticon --version`, before the existing team
  flow. The flow uses that installed CLI where it can. Verify with `make e2e` → `E2E_EXIT:0`.
- [ ] 6.3 Rework `.github/workflows/deploy.yml` into three jobs (D7). The `cli` matrix builds on `ubuntu-22.04`,
  `ubuntu-22.04-arm`, `macos-15` and `windows-2025` with `-p:Version=${tag#v}`, using `fail-fast`. `release`
  (`contents: write`) runs `gh release create` with `panopticon-<rid>[.exe]` and `SHA256SUMS`. `deploy` runs from the
  staged context, `git archive` plus `cli-dist/` with the `.gitignore` lines dropped, using `railway up <dir>
  --path-as-root`. Verify with `actionlint` if it's installed (otherwise a careful read), plus a local dry run of the
  staging script that checks the staged `.gitignore` and `cli-dist/` contents. The real run is in 7.2.

## 7. Docs and human verification

- [ ] 7.1 Update `docs/cli.md` with Install (the script for each OS, `PANOPTICON_INSTALL_DIR`, the glibc ≥ 2.35
  floor, `make install` for source builds), Update (`panopticon update`, restarting the agent) and `--version`. Update
  `README.md`'s Reporting progress section, and `docs/DISCOVERY.md` where it mentions installing. Add an *Amended
  2026-11-11* bullet to ADR-0004: the server serves the binaries, and GitHub Releases are kept as the record.
  Verify by reading each against the spec delta, and with `make validate` → `VALIDATE_EXIT:0`.
- [ ] 7.2 Human verification (stop and ask Emmz): push a release tag. Check that all four `cli` jobs pass, that the
  GitHub Release holds four builds and `SHA256SUMS`, and that `https://panopticon.team/api/cli` reports the tag's
  version with matching SHA-256s. Then run `curl -fsSL https://panopticon.team/install.sh | sh` on a Linux VM and on
  her Mac, `panopticon --version`, and `panopticon update` ("already up to date"). Verify with Emmz's PASS, recorded
  in a devlog note.
- [ ] 7.3 Human verification (stop and ask Emmz): on x64 Windows, run `irm https://panopticon.team/install.ps1 | iex`.
  In a new terminal, run `panopticon --version`. Then `panopticon update` against a server with another version
  replaces the running `.exe`. Verify with Emmz's PASS, or a recorded decision to defer Windows checks, in a devlog
  note.

## Workflow follow-up

- Whole-change review, then offer `code-review`, then offer `/opsx:archive`.
