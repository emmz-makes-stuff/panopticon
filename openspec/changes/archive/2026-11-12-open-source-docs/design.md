# Design

## Context

See proposal.md for why. The current `README.md` (351 lines) is the whole manual: a short intro, then local
development, the user guide (sign-in, Projects, Tags, reporting, change pages, Metrics), configuration, migrations,
the container, the Railway deployment and the release workflow, and the gates. `docs/cli.md` already uses
`panopticon.example.com` and stays where it is. `CLAUDE.md` is the maintainer's build loop and leans on the
maintainer's own `emmz` plugin (`/emmz:pickup`, `/emmz:handoff`, `/emmz:devlog`), which contributors won't have.

This change will be built unattended overnight, so every product question is settled here (the owner's answers from
2026-11-12 are recorded under Decisions). Anything the builder can't settle from this document is a stop, per
`CLAUDE.md`.

## Goals / Non-Goals

**Goals:**
- A newcomer understands what Panopticon is from the README's first screen, and can find the self-hosting guide,
  the user guide and the contribution rules from it.
- Someone can stand up a production server from `docs/self-hosting.md` alone.
- A contributor's agent, of any brand, finds the rules and stops before opening an unwanted pull request.

**Non-Goals:**
- Any code, configuration, build, workflow, `.github/`, `.claude/` or settings change (the only exception is the host
  string in four test files, D6).
- Enforcing the `ready` rule with automation (a workflow that closes PRs). That is configuration; the owner can add it
  later.
- Issue or PR templates under `.github/` (configuration-adjacent; left for the owner).
- Publishing a container image to a registry. The guide builds from the `Dockerfile`.
- Rewriting archived changes, or git history.
- Fixing what the pre-publication audit finds (D8).

## Decisions

**D1. Agent instructions in `AGENTS.md`, with a pointer in `CLAUDE.md`.** `AGENTS.md` is the cross-tool convention
(Codex, Cursor, Copilot, Gemini and others read it). Claude Code reads `CLAUDE.md`, which holds the maintainer's
workflow, so a note goes at its very top, above `# Panopticon`: "Contributing and not the maintainer? Follow
`AGENTS.md`; the workflow below is the maintainer's and relies on tools you won't have." Nothing else in `CLAUDE.md`
changes. *Alternatives:* `AGENTS.md` alone (Claude Code contributors would follow the maintainer loop); splitting
`CLAUDE.md` (changes how the maintainer's own sessions load). Owner chose this one.

`AGENTS.md` contents, in this order: the `ready` rule as a hard stop (no PR without a linked open issue labelled
`ready`; if there isn't one, stop and tell the user); scope (do only what the issue asks; ask on the issue if it's
unclear); the gates (`make gates`, which needs Docker for `make test`; `make deps` first; `make e2e` for image,
sign-in or CLI work), with the rule that a gate passed only if its `LABEL_EXIT:0` was seen; OpenSpec use (a change
under `openspec/changes/<name>/` for anything that changes behaviour, `openspec validate --strict`); conventions from
the repo (`.editorconfig`, analyzers as errors, ADRs in `docs/adrs/` are binding, vanilla TypeScript with no
frameworks in `web/`); commit and PR shape (Conventional Commits as the history uses, `Fixes #N` in the PR body, say
the PR was agent-assisted); and never commit secrets or real hostnames.

**D2. README is a front page; detail moves to `docs/`, wording kept.** The owner chose the split. Sections of the
current README move as whole sections into the new files, edited only for headings, relative links and the host
(D6), not rewritten. Mapping:

| From README | To |
| --- | --- |
| Intro paragraphs, the Development-server warning | README (rewritten as the opening; warning goes to self-hosting and development) |
| Prerequisites, Run it locally, Working on the dashboard, Stopping, Development | `docs/development.md` |
| The first sign-in, Projects, Tags, Reporting progress, Change pages and the history, Metrics | `docs/using.md` |
| Configuration, Migrations, Run it in a container, Deploy to Railway | `docs/self-hosting.md` (expanded, D4) |

The two "After deploying this version" back-fill notes (archived cards; metrics back-fill) are operator concerns: they
go to an "Upgrading" section of `docs/self-hosting.md`, not the user guide. Every in-repo link to a moved anchor
(`README.md#…`, including from `docs/cli.md`, `web/DESIGN.md` and the main specs) is updated to its new home.

**D3. The opening section.** "Ever-so-slightly marketing": confident and warm, not breathless. Under the `PANOPTICON`
title, a one-line tagline, then a short paragraph on the problem (several agents working through OpenSpec changes in
several repos, and no way to see them all at once), then three to five short bullets on what you get (live cards per
change with section progress; change pages with the change's documents; history and how long things take; Projects
and tags for teams; reports through a tiny local MCP bridge, nothing to host on the agent's machine). Then "Getting
on a server": servers grow by invitation, so ask someone who runs one to invite you, or host your own (link). Then
links: using it, connecting an agent (`docs/cli.md`), self-hosting, development, contributing; and the licence line.
No screenshots (none exist in the repo, and adding binaries is out of scope). No emoji. The house style of the
existing docs holds: plain sentences, British spelling, "PANOPTICON" for the brand in headings and email senders,
"Panopticon" in prose as today.

**D4. `docs/self-hosting.md` structure.** In order: *Before you start* (the Development warning; what you need: a
container host, Postgres 17, an S3-compatible bucket that already exists, a Resend account with a verified sending
domain, a public HTTPS URL); *Build the image* (`docker build`, and `cli-dist/` with `make cli-dist-linux` or the
release builds); *Configure* (the configuration table moved from the README, with a Required column, plus a minimal
production env file using `panopticon.example.com` placeholders, `ASPNETCORE_ENVIRONMENT=Production`); *Migrate*
(`--migrate` with the same settings, before each new version); *Run* (generic `docker run` behind a TLS-terminating
proxy, port 8080, `/healthz`); *The first member* (`Panopticon:InitialMembers`, sign in, then invite); *Give your
members the CLI* (what `cli-dist/` is, what the setup guide shows without it, `make cli-dist-linux`, or a release's
builds; `panopticon update` follows the server); *Connect agents* (pointer to `docs/cli.md` and the plugin, which
works with any server); *Deploy to Railway* (the reference deployment, moved); *Releasing from a fork* (what
`deploy.yml` does on a `v*` tag, that the deploy job fails without `RAILWAY_TOKEN` and a Railway project, and the
manual route); *Upgrading* (migrate, then deploy; the back-fill notes); *Backups* (Postgres plus the bucket; both are
needed, as documents live in the bucket). Every setting named comes from the server's code and `appsettings*.json`,
checked by reading them, not from memory.

**D5. Contribution rules.** `CONTRIBUTING.md`, for humans: welcome; issues first (bugs and ideas both); the `ready`
label is applied only by the repository owner(s), and a PR is opened only against an open `ready` issue, linked with
`Fixes #N`; PRs without one are closed unreviewed (ask on the issue instead); agent-written contributions are welcome
under the same rules, and the person opening the PR answers for it; how to set up (`docs/development.md`), the gates,
OpenSpec changes for behaviour, small focused PRs; the licence of contributions (MIT, inbound = outbound, no CLA);
pointers to `CODE_OF_CONDUCT.md` and `SECURITY.md`. "Owner(s)" is said that way because the owner may add
maintainers later.

**D6. The domain scrub.** `panopticon.team` becomes `panopticon.example.com` in `README.md`, `web/DESIGN.md`, the
main specs (through this change's MODIFIED deltas, applied at archive) and the four test files:
`tests/Panopticon.Server.Tests/Distribution/CliEndpointTests.cs`, `web/src/views/projects.test.ts`,
`setupGuide.test.ts`, `tokens.test.ts`. In the tests it is a literal swap, the same everywhere in each file, so the
assertions keep their meaning; the gates prove it. `openspec/changes/archive/` is left as the historical record
(owner's call). `.panopticon.json` names a project id, not a domain, and stays.

**D7. Security and conduct contacts are GitHub-only.** No email address goes in the repo (owner's call).
`SECURITY.md`: supported versions (the latest release only), report through the Security tab's "Report a
vulnerability" (GitHub private vulnerability reporting), what to include, what to expect, and not to open public
issues. `CODE_OF_CONDUCT.md`: Contributor Covenant 2.1 text, verbatim apart from its enforcement contact, which says to
report privately through the same "Report a vulnerability" form with "Code of conduct" in the title, since GitHub has
no other private channel to a repository owner. The text is fetched from the Contributor Covenant site at build time,
not written from memory; if it can't be fetched, that's a stop.

**D8. The pre-publication audit reports; it doesn't fix.** Read-only checks over the working tree and the full git
history (`git log -p --all`): credentials and tokens (key-shaped strings, `RAILWAY_TOKEN`, Resend `re_` keys, AWS
keys, private keys), personal email addresses and names other than the copyright holder, internal or private URLs and
hostnames, and large or binary files committed by mistake. The known public local-only values (`dev-bootstrap-token`,
`panopticon-local-secret`, `dev@localhost.test`) are expected and listed as such. Results go in a devlog note, each
with file or commit, what it is, and a suggested action (history rewrite, rotate, or fine). Nothing found is changed by
this change; anything that needs a fix is the owner's decision in the morning.

**D9. Morning checklist.** The change-done devlog note ends with the owner's go-public checklist: read the audit
findings; create the `ready` label (only owners and collaborators with triage access can apply labels, so it is owner-only while
there are no other collaborators); switch on private vulnerability reporting; check that
`emmz-makes-stuff/panopticon-skills` is public too (self-hosters and contributors need the plugin); merge; make the
repository public.

## Risks / Trade-offs

- [Moved sections break inbound links to `README.md#…` anchors from outside the repo] → Accepted: the repo isn't
  public yet. In-repo links are all updated, and a link check proves it (task 6.1).
- [The self-hosting guide drifts from the code] → Each setting is checked against the code during the build; the
  guide names `appsettings.json` as the source of defaults.
- [Contributor Covenant text written from memory would be subtly wrong] → Fetched, not recalled (D7).
- [A GitHub-only conduct route is awkward] → Stated plainly in D7 and in the morning note; the owner can add an
  address later.
- [The `ready` rule can't be enforced by docs] → `AGENTS.md` makes it a hard stop for agents; automation is a later,
  separate change.
- [Unattended build hits a question] → Decisions above cover the known ones; anything else follows the stop rule in
  `CLAUDE.md` and waits for the morning.
