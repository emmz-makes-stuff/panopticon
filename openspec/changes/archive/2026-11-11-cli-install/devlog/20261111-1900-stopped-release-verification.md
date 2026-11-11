# Stopped: 7.2 and 7.3 need a real release

<!-- Section: 7 · Tasks: 7.1 done (uncommitted), 7.2 and 7.3 open · Base: 9b1bbbb -->

## Where things are

7.1 (the docs) is done and ticked, but **uncommitted**, as a stop requires.

- `docs/cli.md` has a new Install section (per-OS scripts, the install location, platforms and the glibc floor,
  failures, `make install` from source, `--version`). It also gains an Update section (it follows the server, https
  only except loopback, restart the agent, the Windows `.old` file), a line in the intro, and four troubleshooting
  entries.
- `README.md`: Reporting progress now leads with the install command. The container section explains `cli-dist/`. The
  Deploy section describes the three jobs, the staged context, and that a hand deploy has no CLI builds.
- **ADR-0004** has an *Amended 2026-11-11 (cli-install)* bullet, and its "release automation is needed later" line
  now says it's built.
- `docs/DISCOVERY.md` never mentions installing, so it's unchanged.
- `make validate` → `VALIDATE_EXIT:0`.

## Why this is a stop

- **7.2 needs a pushed `v*` tag.** That deploys production and publishes a GitHub Release.
- **The tag has to be on a commit that has the new workflow**, which means merging `change/cli-install` into `main`
  first. So far that has only happened after the whole-change review and archive. Local `main` also holds the
  unpushed CVE fix `1575a72`, which this release would deploy.
- **7.3 needs an x64 Windows machine.**

## The questions

1. Order:
   - (a) the whole-change review and `code-review` first, then merge, push `main`, and tag `v0.2.0` for 7.2 and 7.3; or
   - (b) merge and tag now, and review after.
2. Is there an x64 Windows machine for 7.3, or does Windows wait?

## Known from this session

Everything in the §1–§6 notes, and the 4.3 note, still holds.

## Next

Follow Emmz's answer. For (a): commit 7.1 as part of §7 once 7.2 and 7.3 are settled (or commit the docs now if she
prefers). Then run the whole-change review: `git diff 985fc7c..HEAD` (`985fc7c` is the base of the first section,
the plan commit, rebased to `027894b`).

## Emmz's answer (2026-11-11)

- **Review first, then release.** That means the whole-change review and an offer of `code-review` now, then any fix
  sections, and only then does Emmz merge and push `main` and `v0.2.0`. 7.2 and 7.3 come after that, and the archive
  after them.
- **Windows:** Emmz has an x64 Windows machine, and will run 7.3 once the release is live.
- 7.1 is committed now, separately: `docs(cli-install): … (§7.1)`. This is a deliberate call, because §7 can't
  finish until the release exists, and the review should see the docs.
