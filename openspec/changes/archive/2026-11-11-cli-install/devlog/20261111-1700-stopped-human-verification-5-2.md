# Stopped: §5.2 needs Emmz's review of the install panel

<!-- Section: 5 · Tasks: 5.1 done, 5.2 open · Base: 8908f97 · Work uncommitted -->

## Where things are

§5.1 is done, ticked, and **uncommitted**, as a stop requires. These files are in the working tree:

- `web/src/views/installCli.ts`, which is new:
  - `installCliPanel(env)` returns `{ element, load }`.
  - `installCommand(manifest, os)` and `defaultOs(platform)`.
  - `CliManifest` type.
  - The OS choice is two `aria-pressed` buttons in a `role="group"`, and switching updates in place, so focus stays.
  - Copy and a status line, as on the link file.
  - A 404 shows the `make install` note, and any other failure shows a "Reload" note.
- `web/src/views/tokens.ts`: the panel goes below the token list, and it loads in parallel with the tokens.
- `web/src/shell.ts`: `Env.platform()` and `ShellDeps.platform`, plus `browserPlatform()`, which uses UA Client Hints
  and falls back to `navigator.platform`.
- `web/src/testShell.ts`: a `platform` option, defaulting to `"macOS"`.
- `web/src/views/tokens.test.ts`: 12 new tests (16 in the file).
- `web/src/styles.css`: the `.install-cli*` rules at the end.
- `web/DESIGN.md`: the "Tokens: Install the CLI" bullet.

## The frontend-design pass (done)

It stays inside DESIGN.md: ink primary, never teal, Gabarito subheading, quiet tool styling, and the copy block like
the link file. The one distinctive choice is a muted shell prompt before the command, `$` or `PS>`. It comes from
`data-prompt` through CSS `::before` with `content: attr(data-prompt) " " / ""`, so it can't be selected and screen
readers skip it. In the lead, `panopticon login` is set as `<code>`.

## Gates (with the uncommitted §5 work)

`WEB_BUILD_EXIT:0` `WEB_TEST_EXIT:0` `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0`. The .NET gates were last green at
`8908f97`. §5 changed nothing in .NET.

## The question

Does the "Install the CLI" panel on the Tokens page look and behave right? PASS, or what should change?

The recipe is in the stop message. It uses a stand-in builds directory at
`/private/tmp/claude-501/-Users-rendle-github-emmz-panopticon/f2e7cd4a-7a56-46b5-b3d0-31f925bd4642/scratchpad/cli-builds`.
That holds `version` 0.2.0, `osx-arm64/panopticon` (a copy of the AOT build), and `win-x64/panopticon.exe` (a text
stand-in). It's served through `Panopticon__CliDirectory`.

## Known from this session

Everything in the §1–§4 notes still holds, and so does `20261111-1610-update-https-only.md`. The CVE fix `1575a72` is
on local main, **not pushed**.

## Next

- **On PASS:** tick 5.2, run `make gates`, and write the §5-done note with Emmz's answer. Commit
  `feat(cli-install): install hint on the Tokens page (§5)`, then go to §6, task 6.1.
- **On changes:** make them, re-run the web gates, and ask again.
