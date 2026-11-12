# Security policy

Panopticon signs people in, issues API tokens and stores the documents of other people's changes, so security reports
are very welcome. Please report them privately, never in a public issue, discussion or pull request.

## Supported versions

Only the latest release (the newest `v*` tag) gets security fixes. The server and the CLI share a version, and a fix
ships as a new release of both. If you run your own server, upgrade it to the new release
([`docs/self-hosting.md`](docs/self-hosting.md#upgrading)), and your members' `panopticon update` follows it.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: on this repository's **Security** tab, choose **Report a
vulnerability** (or go straight to
[the form](https://github.com/emmz-makes-stuff/panopticon/security/advisories/new)). Only you and the repository's
maintainers can see the report.

Please include:

- what an attacker can do, and what they need first (a member's session, an API token, a Project role, network
  access to the server);
- the steps to reproduce it, against a local server if you can (see [`docs/development.md`](docs/development.md));
- the version (`panopticon --version`, or the release tag of the server);
- anything you know about a fix.

## What happens next

You'll get a reply on the report. Panopticon is maintained in spare time, so there's no fixed response time, but a
real vulnerability takes priority over everything else. Once it's confirmed, the fix is worked on in the advisory's
temporary private fork, released, and then the advisory is published, crediting you unless you'd rather not be named.

## In scope, and not

In scope: the server (sign-in, sessions, API tokens, Project and member permissions, who sees which change), the MCP
endpoint, the `panopticon` CLI and bridge, the install scripts the server hands out, and the `Dockerfile`.

Not vulnerabilities, because they're documented behaviour:

- A server running as **Development** logs sign-in links instead of emailing them, and accepts a public bootstrap
  token. That's for a local machine only, and the server won't start with a bootstrap token set in any other environment.
- Problems in a particular server's own hosting (its proxy, database or bucket settings). Report those to whoever runs
  that server.
