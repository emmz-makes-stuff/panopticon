# Spec Delta

## MODIFIED Requirements

### Requirement: Version manifest
The server SHALL answer `GET /api/cli`, with no sign-in, with the version of the CLI builds it serves, the install
script URLs, and each platform's download URL, size and SHA-256. Every URL SHALL be absolute and built on
`Panopticon:PublicUrl`.

#### Scenario: Manifest on the public server
- **WHEN** an anonymous client asks `https://panopticon.example.com/api/cli` on a server with `0.2.0` builds
- **THEN** it gets version `0.2.0`, `https://panopticon.example.com/install.sh`, `https://panopticon.example.com/install.ps1`, and
  for `linux-x64` the URL `https://panopticon.example.com/cli/linux-x64/panopticon` with that file's size and SHA-256

#### Scenario: URLs ignore the request's host
- **WHEN** `Panopticon:PublicUrl` is `https://panopticon.example.com` and the request arrives with another `Host`
- **THEN** every URL in the manifest starts with `https://panopticon.example.com`

### Requirement: Unix install script
`GET /install.sh` SHALL return a POSIX `sh` script, with no sign-in, that works out the OS and CPU and downloads the
matching build from this server. It SHALL check the SHA-256 and install the file as `panopticon`. On an unsupported
platform, or a checksum mismatch, it SHALL stop with an error and change nothing.

#### Scenario: Install on a Linux VM
- **WHEN** someone runs `curl -fsSL https://panopticon.example.com/install.sh | sh` on an arm64 Linux VM without .NET
- **THEN** `~/.local/bin/panopticon` is the `linux-arm64` build, `panopticon --version` prints the server's CLI
  version, and the script prints `panopticon login --server https://panopticon.example.com` as the next step

#### Scenario: Unsupported platform
- **WHEN** the script runs on an Intel Mac
- **THEN** it exits non-zero, names the platform as unsupported, and installs nothing

#### Scenario: musl Linux
- **WHEN** the script runs on Alpine Linux (musl libc)
- **THEN** it exits non-zero, saying only glibc Linux is supported, and installs nothing

#### Scenario: Corrupted download
- **WHEN** the downloaded file's SHA-256 doesn't match the manifest
- **THEN** the script exits non-zero with a checksum error, and any existing `panopticon` is left as it was

### Requirement: Windows install script
`GET /install.ps1` SHALL return a PowerShell script, with no sign-in, that downloads the `win-x64` build from this
server, checks its SHA-256, installs it to `%LOCALAPPDATA%\Programs\panopticon\panopticon.exe`, and adds that
directory to the user's `PATH` if it's missing. It SHALL print the `panopticon login --server` command to run next.

#### Scenario: Install on Windows
- **WHEN** someone runs `irm https://panopticon.example.com/install.ps1 | iex` on x64 Windows
- **THEN** `panopticon.exe` is installed, a new terminal finds `panopticon` on `PATH`, and the script prints
  `panopticon login --server https://panopticon.example.com`

#### Scenario: Windows on Arm
- **WHEN** the script runs on arm64 Windows
- **THEN** it exits with an error naming the platform as unsupported, and installs nothing

### Requirement: Install hint on the Tokens page
The Tokens page SHALL show the one-line install command for the visitor's OS, as the first step of the setup guide
(machine-setup), from the manifest's script URLs. It SHALL offer a way to copy it, and a way to see the other OS's
command. The commands are `curl -fsSL <url> | sh` and `irm <url> | iex`. On a server without CLI builds, it SHALL say so
and point to `make install`.

#### Scenario: Visitor on macOS
- **WHEN** a member on macOS opens the Tokens page of `https://panopticon.example.com`
- **THEN** it shows `curl -fsSL https://panopticon.example.com/install.sh | sh` with a copy button, and the Windows command
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
  `https://panopticon.example.com/api/cli` reports version `0.2.0` with the same SHA-256s

#### Scenario: One platform fails to build
- **WHEN** the macOS build fails for a tag
- **THEN** no GitHub Release is created and the server isn't deployed

