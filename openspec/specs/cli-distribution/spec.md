# cli-distribution Specification

## Purpose

How a machine gets the `panopticon` CLI and keeps it current. The server hands out install scripts, binaries and a
version manifest for its own release. The CLI updates itself from the server it's logged in to.

## Requirements

### Requirement: Supported platforms
A release SHALL include the CLI for `linux-x64`, `linux-arm64`, `osx-arm64` and `win-x64`. The Windows file SHALL be
`panopticon.exe`, and the others `panopticon`. Each build SHALL be stamped with the release version.

#### Scenario: Release contents
- **WHEN** the release for `v0.2.0` is built
- **THEN** it has a build for each of the four platforms, and each one's `panopticon --version` prints `0.2.0`

### Requirement: Version manifest
The server SHALL answer `GET /api/cli`, with no sign-in, with the version of the CLI builds it serves, the install
script URLs, and each platform's download URL, size and SHA-256. Every URL SHALL be absolute and built on
`Panopticon:PublicUrl`.

#### Scenario: Manifest on the public server
- **WHEN** an anonymous client asks `https://panopticon.team/api/cli` on a server with `0.2.0` builds
- **THEN** it gets version `0.2.0`, `https://panopticon.team/install.sh`, `https://panopticon.team/install.ps1`, and
  for `linux-x64` the URL `https://panopticon.team/cli/linux-x64/panopticon` with that file's size and SHA-256

#### Scenario: URLs ignore the request's host
- **WHEN** `Panopticon:PublicUrl` is `https://panopticon.example.com` and the request arrives with another `Host`
- **THEN** every URL in the manifest starts with `https://panopticon.example.com`

### Requirement: Binary downloads
The server SHALL serve each platform's CLI build at `/cli/<platform>/<file>` with no sign-in, as a binary download
whose bytes match the manifest's SHA-256. An unknown platform or file SHALL be a 404.

#### Scenario: Download a build
- **WHEN** an anonymous client gets `/cli/osx-arm64/panopticon`
- **THEN** the response is the macOS build, and its SHA-256 matches the manifest's entry for `osx-arm64`

#### Scenario: Unknown platform
- **WHEN** a client gets `/cli/freebsd-x64/panopticon`
- **THEN** the response is 404, not the web UI's page

### Requirement: Server without CLI builds
A server started without CLI builds SHALL keep working. It SHALL answer `/api/cli` with 404 and a message saying this
server has no CLI builds, and `/install.sh`, `/install.ps1` and `/cli/...` SHALL answer 404. It SHALL NOT fail to start
or serve the web UI's page in their place.

#### Scenario: Local image
- **WHEN** the image is built and run without CLI builds, and a client gets `/install.sh`
- **THEN** the response is 404 with a message saying this server has no CLI builds and to build from source with
  `make install`

### Requirement: Unix install script
`GET /install.sh` SHALL return a POSIX `sh` script, with no sign-in, that works out the OS and CPU and downloads the
matching build from this server. It SHALL check the SHA-256 and install the file as `panopticon`. On an unsupported
platform, or a checksum mismatch, it SHALL stop with an error and change nothing.

#### Scenario: Install on a Linux VM
- **WHEN** someone runs `curl -fsSL https://panopticon.team/install.sh | sh` on an arm64 Linux VM without .NET
- **THEN** `~/.local/bin/panopticon` is the `linux-arm64` build, `panopticon --version` prints the server's CLI
  version, and the script prints `panopticon login --server https://panopticon.team` as the next step

#### Scenario: Unsupported platform
- **WHEN** the script runs on an Intel Mac
- **THEN** it exits non-zero, names the platform as unsupported, and installs nothing

#### Scenario: musl Linux
- **WHEN** the script runs on Alpine Linux (musl libc)
- **THEN** it exits non-zero, saying only glibc Linux is supported, and installs nothing

#### Scenario: Corrupted download
- **WHEN** the downloaded file's SHA-256 doesn't match the manifest
- **THEN** the script exits non-zero with a checksum error, and any existing `panopticon` is left as it was

### Requirement: Install location
The Unix script SHALL install to `~/.local/bin`, or to `PANOPTICON_INSTALL_DIR` when set, creating the directory if
needed. It SHALL replace an existing `panopticon` there. If the directory isn't on `PATH`, it SHALL say so and show
the line to add.

#### Scenario: Custom directory
- **WHEN** `PANOPTICON_INSTALL_DIR=/opt/bin` is set and the script runs
- **THEN** the CLI is installed as `/opt/bin/panopticon`

#### Scenario: Directory not on PATH
- **WHEN** `~/.local/bin` isn't on `PATH`
- **THEN** the script still installs, and says how to add `~/.local/bin` to `PATH`

### Requirement: Windows install script
`GET /install.ps1` SHALL return a PowerShell script, with no sign-in, that downloads the `win-x64` build from this
server, checks its SHA-256, installs it to `%LOCALAPPDATA%\Programs\panopticon\panopticon.exe`, and adds that
directory to the user's `PATH` if it's missing. It SHALL print the `panopticon login --server` command to run next.

#### Scenario: Install on Windows
- **WHEN** someone runs `irm https://panopticon.team/install.ps1 | iex` on x64 Windows
- **THEN** `panopticon.exe` is installed, a new terminal finds `panopticon` on `PATH`, and the script prints
  `panopticon login --server https://panopticon.team`

#### Scenario: Windows on Arm
- **WHEN** the script runs on arm64 Windows
- **THEN** it exits with an error naming the platform as unsupported, and installs nothing

### Requirement: Install scripts name their server
Each install script SHALL download only from the server that served it. It SHALL use URLs built on that server's
`Panopticon:PublicUrl`, so a self-hosted server installs its own release.

#### Scenario: Self-hosted server
- **WHEN** `https://panopticon.example.com/install.sh` is run
- **THEN** every download it makes is from `https://panopticon.example.com`, and the next step it prints is
  `panopticon login --server https://panopticon.example.com`

### Requirement: Version command
`panopticon --version` SHALL print the CLI's version and exit 0.

#### Scenario: Print the version
- **WHEN** someone runs `panopticon --version` on a `0.2.0` build
- **THEN** it prints `0.2.0` and exits 0

### Requirement: Self-update
`panopticon update` SHALL fetch the manifest from its server and download this platform's build. It SHALL check the
SHA-256 and replace its own binary, then print the old and new versions. The server comes from `--server`, then
`PANOPTICON_SERVER_URL`, then the saved login, as for `panopticon login`. It SHALL need no token.

#### Scenario: Newer build on the server
- **WHEN** a `0.1.1` CLI logged in to a server with `0.2.0` builds runs `panopticon update`
- **THEN** the binary on disk becomes the `0.2.0` build, and it prints that it updated from `0.1.1` to `0.2.0`

#### Scenario: Already current
- **WHEN** the CLI's version equals the manifest's
- **THEN** it says it is already up to date, changes nothing, and exits 0

#### Scenario: Server has an older build
- **WHEN** a `0.3.0` CLI runs `panopticon update` against a server with `0.2.0` builds
- **THEN** it installs the `0.2.0` build, so the CLI matches the server it reports to

#### Scenario: No server known
- **WHEN** there is no `--server`, no `PANOPTICON_SERVER_URL` and no saved login
- **THEN** it exits non-zero, saying to pass `--server` or run `panopticon login`

### Requirement: Safe self-update
`panopticon update` SHALL leave the existing binary working whenever it fails. That covers a server that can't be
reached, a server without CLI builds, a manifest with no build for this platform, a checksum mismatch, and a
directory it can't write to. It SHALL exit non-zero with a message naming the problem.

#### Scenario: Checksum mismatch
- **WHEN** the downloaded build's SHA-256 doesn't match the manifest
- **THEN** it exits non-zero with a checksum error, and the existing binary still runs

#### Scenario: Server without CLI builds
- **WHEN** the server answers `/api/cli` with 404
- **THEN** it exits non-zero, saying the server has no CLI builds, and changes nothing

#### Scenario: Running on Windows
- **WHEN** `panopticon update` runs on Windows while the binary is in use
- **THEN** the new build takes its place, and the next `panopticon` run is the new version

### Requirement: Updates only over HTTPS
`panopticon update` SHALL refuse, before downloading anything, a server or a build URL that isn't `https`, unless its
host is this machine (`localhost`, `127.0.0.1`, `::1`). The manifest and its checksums come over the same connection
as the build, so plain http would let anyone on the path swap both.

#### Scenario: Plain http server
- **WHEN** someone runs `panopticon update --server http://panopticon.example.com`
- **THEN** it exits non-zero, saying it needs an https server, makes no request, and changes nothing

#### Scenario: Plain http build URL
- **WHEN** an https server's manifest gives an `http://` URL for this platform's build
- **THEN** it exits non-zero without downloading it, and the binary is unchanged

#### Scenario: Local server
- **WHEN** someone runs `panopticon update --server http://localhost:7480`
- **THEN** the update goes ahead over http

### Requirement: Install hint on the Tokens page
The Tokens page SHALL show the one-line install command for the visitor's OS, from the manifest's script URLs, with
a way to copy it and to see the other OS's command. The commands are `curl -fsSL <url> | sh` and `irm <url> | iex`.
On a server without CLI builds it SHALL say so and point to `make install`.

#### Scenario: Visitor on macOS
- **WHEN** a member on macOS opens the Tokens page of `https://panopticon.team`
- **THEN** it shows `curl -fsSL https://panopticon.team/install.sh | sh` with a copy button, and the Windows command
  is one click away

#### Scenario: Server without CLI builds
- **WHEN** the server has no CLI builds
- **THEN** the Tokens page says this server has no CLI builds and to build from source with `make install`, and shows
  no install command

### Requirement: Release publishes the builds
Pushing a `v*` tag SHALL build the CLI for every supported platform with that version, and publish a GitHub Release
for the tag with the four builds and a `SHA256SUMS` file. The server deployed from that tag SHALL bundle and serve
exactly those builds. If any platform's build fails, nothing SHALL be published or deployed.

#### Scenario: Tag a release
- **WHEN** `v0.2.0` is pushed
- **THEN** a GitHub Release `v0.2.0` holds the four builds and `SHA256SUMS`, and once the deploy finishes,
  `https://panopticon.team/api/cli` reports version `0.2.0` with the same SHA-256s

#### Scenario: One platform fails to build
- **WHEN** the macOS build fails for a tag
- **THEN** no GitHub Release is created and the server isn't deployed
