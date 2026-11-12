# Proposal

## Why

The repository goes public on 2026-11-13. The hosted server will grow by invitation only, so anyone outside that circle
needs to be able to run their own instance from the docs alone. Contributors, and the agents many of them will use,
need clear rules, the main one being that pull requests are only for issues the owner has marked `ready`. Today the
README mixes the user guide, local development and the Railway deployment, there is no licence, and nothing tells an
outside contributor (or their agent) how to work here.

## What Changes

Documentation only. No code, configuration, build, workflow or settings file changes, apart from test literals (below).

- **LICENSE**: MIT, `Copyright (c) 2026 Emmz`, matching OpenSpec.
- **README.md** becomes a short front page: a slightly marketing opening section (what Panopticon is and why you'd
  want it), how it works in a few lines, how to get on a server (ask someone with one to invite you, or host your
  own), and links into `docs/`. Its current detail moves, with its wording kept, into:
  - `docs/self-hosting.md` (new and complete): what you need (Postgres, an S3-compatible bucket, Resend and a
    domain), building the image, configuration, migrations, the first member, CLI builds for your users, the generic
    container route and the Railway reference deployment, upgrades, and the release workflow for forks.
  - `docs/using.md`: sign-in, Projects, Tags, reporting progress, change pages and history, Metrics.
  - `docs/development.md`: running locally, working on the dashboard, gates, `make e2e`, the build workflow.
- **CONTRIBUTING.md**: issues first; pull requests only against an issue carrying the `ready` label, which only the
  repository owner(s) apply; agent-generated contributions welcome under the same rules; gates must pass; how a PR is
  shaped.
- **AGENTS.md**: the contributor rules written for coding agents (the cross-tool convention), including the `ready`
  gate as a hard stop, the gates, and the OpenSpec layout.
- **CLAUDE.md**: one note added at the top sending anyone who isn't the maintainer to `AGENTS.md`. The maintainer's
  workflow below it is unchanged.
- **SECURITY.md**: report vulnerabilities privately through GitHub's private vulnerability reporting, never in a
  public issue.
- **CODE_OF_CONDUCT.md**: Contributor Covenant 2.1, reports through GitHub only (no email address in the repo).
- **No public-server domain** anywhere in the working tree outside `openspec/changes/archive/`: the README,
  `web/DESIGN.md`, the main specs (through this change's deltas) and four test files use `panopticon.example.com`
  instead. In the tests that is the example host string only; behaviour and assertions are otherwise unchanged.
- **Pre-publication audit**: a read-only sweep of the tree and the git history for secrets, personal details and
  private links. Findings are reported in the devlog for the owner to act on; this change fixes nothing it finds
  beyond its own scope.

## Capabilities

### New Capabilities

- `contribution-policy`: the licence, how contributions are accepted (the `ready`-label gate), instructions for
  contributing agents, conduct and security reporting.
- `self-hosting-guide`: what the repository promises someone standing up their own server: a complete guide that
  needs nothing else, and no dependency on any particular public server.

### Modified Capabilities

- `cli-distribution`: the example server in the scenarios of Version manifest, Unix install script, Windows install
  script, Install hint on the Tokens page and Release publishes the builds becomes `panopticon.example.com`. No
  behaviour changes.
- `machine-setup`: the same for the Three setup steps scenario.

## Impact

- New files at the root (`LICENSE`, `CONTRIBUTING.md`, `AGENTS.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`) and in
  `docs/` (`self-hosting.md`, `using.md`, `development.md`). `README.md` rewritten; `CLAUDE.md` gains a note;
  `web/DESIGN.md` and `docs/cli.md` get link and host fixes.
- Tests: `tests/Panopticon.Server.Tests/Distribution/CliEndpointTests.cs`, `web/src/views/projects.test.ts`,
  `setupGuide.test.ts`, `tokens.test.ts` change one host string. All gates must stay green.
- Repository settings the owner applies by hand before going public (not in this change): create the `ready` label,
  switch on private vulnerability reporting, make the repo public. The change leaves a checklist for them.
