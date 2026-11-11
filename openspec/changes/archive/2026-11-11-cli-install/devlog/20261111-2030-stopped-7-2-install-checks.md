# Stopped: 7.2's machine checks, after a clean v0.2.0 release

<!-- Section: 7 · Tasks: 7.2 (in progress), 7.3 open · Released: v0.2.0 = fe49a27 on main -->

## Done (Emmz said "you can run the release")

- `change/cli-install` merged into `main` with `--no-ff` as `fe49a27`. `main` pushed (`4b5f1d5..fe49a27`), which
  carries the CVE-2026-114149 fix with it. `v0.2.0` tagged and pushed.
- Workflow run `43028649209`: `CLI (linux-x64)`, `CLI (linux-arm64)`, `CLI (osx-arm64)` and `CLI (win-x64)` all
  succeeded, then `release` and `deploy`. This confirms three things that were unverified until now:
  `ubuntu-22.04-arm` on the private repo, `gh release create` with the workflow token, and
  `railway up <dir> --path-as-root` on CLI 5.71.
- Release `v0.2.0` assets: `panopticon-linux-arm64`, `panopticon-linux-x64`, `panopticon-osx-arm64`,
  `panopticon-win-x64.exe` and `SHA256SUMS`.
- `https://panopticon.team/api/cli`: 200, version `0.2.0`, install URLs on `https://panopticon.team`. Every platform's
  `sha256` equals `SHA256SUMS`:
  - linux-x64 `fc996ffc…380d`
  - linux-arm64 `06170331…9ef8`
  - osx-arm64 `1abe7755…5349`
  - win-x64 `2a03a875…ef14`
- GET `/cli/osx-arm64/panopticon`: 200 `application/octet-stream`, `Cache-Control: no-cache`, 14475784 bytes, and its
  SHA-256 matches. `/install.sh`: `text/plain`, `no-cache`, "Installs the Panopticon CLI 0.2.0 from
  https://panopticon.team".

## Found in production (not blocking; fix in a later change, or note it)

- **HEAD `/cli/<rid>/<file>` returns 200 `text/html`** (the app shell). The endpoints use `MapGet`, so HEAD falls
  through to the SPA fallback. The spec only covers GET, and every client uses GET. A follow-up fix is to map HEAD
  too, or exclude `cli` from the fallback.
- **There's no `ETag` on downloads in production** (seen on a GET), so the 9.2 comment ("ETag and Last-Modified keep
  that check cheap") overstates it. `no-cache` still forces revalidation, which is what matters.

## The question for Emmz (7.2's checks on her machines)

On a glibc Linux VM (arm64 or x64), and on her Mac:

```sh
curl -fsSL https://panopticon.team/install.sh | sh
```

```sh
~/.local/bin/panopticon --version
```

```sh
~/.local/bin/panopticon update --server https://panopticon.team
```

Expected:
- the install prints `Installed panopticon 0.2.0 to …/.local/bin/panopticon` and `panopticon login --server https://panopticon.team`, plus a PATH hint if `~/.local/bin` isn't on `PATH`;
- `--version` prints `0.2.0`;
- `update` prints `panopticon 0.2.0 is already up to date with https://panopticon.team.`

On PASS: tick 7.2, record it here, and give her 7.3 (Windows: `irm https://panopticon.team/install.ps1 | iex`, a new
terminal, `panopticon --version`).
On FAIL: record the output, and diagnose.

## Next

- Wait for Emmz's 7.2 results. Then 7.3, then offer `/opsx:archive`, then offer to draft the onboarding-guide change
  (see the §9 note), and mention the two production findings above as candidates for it or for a small fix change.
