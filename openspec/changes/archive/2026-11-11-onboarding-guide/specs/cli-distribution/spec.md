# Spec Delta

## MODIFIED Requirements

### Requirement: Install hint on the Tokens page
The Tokens page SHALL show the one-line install command for the visitor's OS, as the first step of the setup guide
(machine-setup), from the manifest's script URLs. It SHALL offer a way to copy it, and a way to see the other OS's
command. The commands are `curl -fsSL <url> | sh` and `irm <url> | iex`. On a server without CLI builds, it SHALL say so
and point to `make install`.

#### Scenario: Visitor on macOS
- **WHEN** a member on macOS opens the Tokens page of `https://panopticon.team`
- **THEN** it shows `curl -fsSL https://panopticon.team/install.sh | sh` with a copy button, and the Windows command
  is one click away

#### Scenario: Server without CLI builds
- **WHEN** the server has no CLI builds
- **THEN** the Tokens page says this server has no CLI builds and to build from source with `make install`, and shows
  no install command

## ADDED Requirements

### Requirement: CLI endpoints answer HEAD
`/api/cli`, `/install.sh`, `/install.ps1` and `/cli/<platform>/<file>` SHALL answer HEAD with the same status and
headers as GET, and no body. They SHALL never answer it with the web UI's page.

#### Scenario: HEAD on a build
- **WHEN** a client sends HEAD for `/cli/osx-arm64/panopticon`
- **THEN** the response is 200 `application/octet-stream`, with the build's `Content-Length`, and no body

#### Scenario: HEAD on an unknown build
- **WHEN** a client sends HEAD for `/cli/freebsd-x64/panopticon`
- **THEN** the response is 404, not the web UI's page

### Requirement: Build downloads carry validators
Each build download SHALL send an `ETag` derived from the build's SHA-256, and a `Last-Modified`, alongside
`Cache-Control: no-cache`. A request whose `If-None-Match` matches that ETag SHALL get a 304 with no body.

#### Scenario: Revalidation
- **WHEN** a client asks for `/cli/linux-x64/panopticon` with `If-None-Match` set to the ETag of an earlier response
  for the same build
- **THEN** the response is 304 with no body

#### Scenario: New release
- **WHEN** the server bundles a different build for `linux-x64`
- **THEN** its ETag differs from the previous build's, so a revalidation downloads the new bytes
