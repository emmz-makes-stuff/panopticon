# Tasks

Documentation only, built unattended. No code, configuration, build, workflow, `.github/`, `.claude/` or settings
changes, apart from the host string in four test files (design D6). A product question that design.md doesn't settle
is a stop (CLAUDE.md "Stop and ask"): leave a `stopped-…` devlog note and wait for the morning.

## 1. Pre-publication audit (read-only)

- [x] 1.1 Sweep the working tree and the full history (`git log -p --all`) for credentials and tokens (key-shaped
  strings, Resend `re_` keys, AWS keys, Railway tokens, private keys), personal email addresses and names other than
  the copyright holder, private URLs and hostnames, and large or binary files committed by mistake (design D8). Verify:
  every check's command and hit count are recorded, and `git status` shows no change outside the change folder.
- [x] 1.2 Write the findings as an `audit` devlog note: each hit with file or commit, what it is, and a suggested
  action (fine / rotate / rewrite history), with the known public local-only values listed as expected. Verify: the
  note exists and names every check from 1.1, including those with no hits.

## 2. Domain scrub

- [x] 2.1 Replace `panopticon.team` with `panopticon.example.com` in
  `tests/Panopticon.Server.Tests/Distribution/CliEndpointTests.cs`, `web/src/views/projects.test.ts`,
  `web/src/views/setupGuide.test.ts` and `web/src/views/tokens.test.ts`, the host string only. Verify: `git diff`
  shows only that string changing, and `make test` and `make web-test` pass.
- [x] 2.2 Do the same in `web/DESIGN.md`. Verify: `git grep -n panopticon.team -- ':!openspec/changes/archive'` lists
  only `README.md` (rewritten in §4) and the main specs (updated by this change's deltas at archive).

## 3. Licence, conduct and security

- [x] 3.1 Add `LICENSE`: the standard MIT text with `Copyright (c) 2026 Emmz`. Verify: it matches the SPDX MIT
  template word for word apart from the copyright line.
- [x] 3.2 Add `CODE_OF_CONDUCT.md`: the Contributor Covenant 2.1, fetched from contributor-covenant.org (not written
  from memory; a failed fetch is a stop), with the GitHub-only enforcement contact from design D7. Verify: apart from
  the contact, it matches the fetched text, and `grep -c '@' CODE_OF_CONDUCT.md` finds no email address.
- [x] 3.3 Add `SECURITY.md` per design D7. Verify: it covers supported versions, the private reporting route, what to
  include and what to expect, and names no email address.

## 4. README front page and the docs split

- [x] 4.1 Create `docs/using.md` and `docs/development.md` by moving the README sections in design D2's table, whole
  and with wording kept; fix relative links for the new location. Verify: a diff of each moved section against the old
  README shows only headings, links and the host changing.
- [x] 4.2 Create `docs/self-hosting.md` per design D4, from the moved Configuration, Migrations, container and Railway
  sections plus the new parts. Check every setting against `src/Panopticon.Server` (its options classes and
  `appsettings*.json`) and every command against the `Makefile`, `Dockerfile` and `.github/workflows/deploy.yml`.
  Verify: each scenario of the `self-hosting-guide` spec is satisfied by a named section, recorded in a devlog note,
  and no setting the server reads outside Development is missing from the table.
- [x] 4.3 Rewrite `README.md` as the front page per design D3: opening section, how it works, getting on a server,
  links, licence. Verify: it has no section left over from the old manual, links every new doc, and fits in about
  80 lines.
- [x] 4.4 Update every in-repo link to a moved README anchor (`docs/cli.md`, `web/DESIGN.md`, `docs/`, and any spec or
  ADR outside the archive). Verify: `git grep -n 'README.md#' -- ':!openspec/changes/archive'` finds only anchors
  that still exist in the new README.

## 5. Contribution rules

- [x] 5.1 Add `CONTRIBUTING.md` per design D5. Verify: each scenario of the `contribution-policy` spec's Pull requests
  only for ready issues and Agent-generated contributions requirements is answered by a named paragraph.
- [x] 5.2 Add `AGENTS.md` per design D1, with gate commands copied from the `Makefile`. Verify: the `ready` stop is the
  first rule, and every command it names exists as a `Makefile` target or an installed tool in `docs/dependencies.md`.
- [x] 5.3 Add the contributor note at the very top of `CLAUDE.md` (design D1), changing nothing else. Verify:
  `git diff CLAUDE.md` shows only added lines, all above `# Panopticon`.

## 6. Final checks

- [x] 6.1 Check every relative Markdown link and anchor in the root `*.md` files and `docs/`, `web/DESIGN.md` with a
  scratchpad script (no new dependency). Verify: the script reports zero broken links.
- [x] 6.2 Confirm the change's scope: `git diff --stat <first-section-base>..HEAD` touches only Markdown files,
  `LICENSE`, the change folder and the four test files from 2.1, and
  `git grep -n panopticon.team -- ':!openspec/changes/archive' ':!openspec/specs'` finds nothing. Verify: both
  outputs quoted in the section-done note.
- [x] 6.3 Write the change-done devlog note with the owner's go-public checklist (design D9) and a summary of the
  audit findings. Verify: the note's `## Next` is the checklist, and `make gates` is green.

## Workflow follow-up

- Offer the `code-review` skill on the branch, then `/opsx:archive` once the owner says yes (this applies the spec
  deltas, which removes the domain from `openspec/specs/`).
- The owner applies the repository settings in the morning and makes the repository public.
