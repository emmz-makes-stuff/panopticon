# Tasks

## 1. CLI downloads: HEAD and validators

- [x] 1.1 Map `/api/cli`, `/install.sh`, `/install.ps1` and `/cli/{platform}/{file}` for GET and HEAD (design D6).
  Verify with `CliEndpointTests` cases:
  - HEAD `/cli/osx-arm64/panopticon` gives 200 `application/octet-stream`, the build's `Content-Length`, and an empty
    body;
  - HEAD `/cli/freebsd-x64/panopticon` gives 404, not `text/html`;
  - HEAD `/install.sh` gives 200 `text/plain`;
  - HEAD `/api/cli` gives 200 JSON with no body.
- [x] 1.2 Add `LastModified` to `CliBuild`, read at catalogue load. Pass an ETag made from the build's SHA-256, plus
  `lastModified`, to the download (design D7), and fix the 9.2 comment on `Download`. Verify with tests:
  - the ETag is `"<sha256>"`;
  - a `Last-Modified` is present;
  - a matching `If-None-Match` gives 304 with no body;
  - a rebuilt file with other bytes gets a different ETag.

## 2. Setup guide on the Tokens page

- [x] 2.1 Create `web/src/views/setupGuide.ts` from `installCli.ts`, and delete `installCli.ts`. It has three numbered
  steps (spec machine-setup):
  - Install: the existing tabs, copy and no-builds note.
  - Log in: `panopticon login --server <url>`, with the server from the manifest's `installSh` origin, or
    `location.origin` on a 404 (design D2).
  - Connect: the three plugin commands, each with Copy, plus an "Other agents" disclosure holding the `.mcp.json`
    snippet with Copy (D5).

  Done marks come from the tokens passed in (D3). Verify with `vitest` tests:
  - the step order and text;
  - the self-hosted server URL in the log-in command;
  - the no-builds install step, with the other two steps still shown;
  - each command copies exactly its constant;
  - done marks for no tokens, a token never used, and a token that has reported.
- [x] 2.2 The Tokens page shows the guide in place of the old panel, passes it the loaded tokens, and re-renders it
  after a revoke. Verify with `tokens.test.ts`:
  - the guide shows with every step done when all tokens have reported;
  - revoking the only reported token un-marks connect;
  - the existing install-panel tests still pass, moved to the guide.
- [x] 2.3 Run the `frontend-design` pass on the guide (D9). Update `web/DESIGN.md` with a "Setup guide" section that
  replaces the "Tokens: Install the CLI" bullet, and add the styles to `styles.css`. Verify with `make web-build`,
  `make web-lint` and `make web-format` green, and that the section matches what renders.

## 3. Projects page and Project page

- [x] 3.1 The Projects page fetches `/api/tokens` with `/api/projects`. It shows the guide while no token has reported
  and `panopticon.setupGuide.hidden` isn't set, with a Hide control that sets that key (D4). Verify with
  `projects.test.ts`:
  - a new member sees the guide;
  - Hide removes it and keeps it removed on the next open;
  - a reporting member doesn't see it;
  - storage that throws still renders the guide, and Hide then hides it for the current page only;
  - a failed tokens request still shows the Projects list.
- [x] 3.2 Below the Project page's link file, add the line "Or ask your agent to link this repo to <CODE>: it can look
  the Project up and write the file." (spec projects, D8). Verify with a `projects.test.ts` case on `PAN`'s page.

## 4. Docs

- [x] 4.1 `docs/cli.md` gets the plugin route in its "Claude Code" section: the marketplace add, the install and
  `/panopticon:setup`, ahead of the `.mcp.json` and `claude mcp add` alternatives. Its "Install" section mentions the
  guide on the Tokens page. `README.md`'s Reporting progress section gets the three steps. Verify by reading both
  against the spec delta, and check that every command matches a constant in `setupGuide.ts`.

## 5. Human verification

- [x] 5.1 Human verification (stop and ask Emmz): run the server locally (`make` dev flow, or `docker compose`), sign
  in as a member with no tokens, and check:
  - the Projects page and the Tokens page show the guide;
  - Hide works on Projects;
  - after `panopticon login` (step 2) and a report (step 3), the steps show as done on reload;
  - the look matches the `frontend-design` result on desktop and at phone width;
  - the Project page shows the link hint.

  Verify with Emmz's PASS, recorded in a devlog note.
- [x] 5.2 (Deferred to the next release by Emmz, 2026-11-11; see the change-done note.) After the next release is
  deployed, check against `https://panopticon.team`:
  - HEAD `/cli/linux-x64/panopticon` gives 200 `application/octet-stream`;
  - a GET has an ETag that equals `"<sha256>"` from `/api/cli`;
  - repeating it with `If-None-Match` gives 304.

  Verify with the commands' output recorded in a devlog note, or a recorded decision to defer it to the release.

## 6. Review fixes: the Hidden scenario on the Tokens page

- [x] 6.1 Add a `tokens.test.ts` case for the second half of machine-setup's "Hidden" scenario. With
  `SETUP_GUIDE_HIDDEN_KEY` set to `"1"` in `local`, the Tokens page still shows `.setup-guide`. Verify with
  `make web-test`.

## 7. Review fixes: code-review cleanups

- [ ] 7.1 Connect's done-ness stays on `lastUsedAt`, as D3 has it. The token being used by any call counts, not only
  a report (Emmz, 2026-11-11). Reword machine-setup's "Steps marked done" and "Guide on the Projects page"
  requirements and scenarios from "has reported" to "has been used", and note in D3 that this is a stand-in. Verify
  with `make validate`.
- [ ] 7.2 Move `SETUP_GUIDE_HIDDEN_KEY` from `projects.ts` to `setupGuide.ts`, and import it from there in
  `projects.ts` and both test files. Verify with `make web-test`.
- [ ] 7.3 `serverUrl` returns `new URL("/", manifest.installSh)`'s origin instead of regex-replacing `/install.sh`,
  and the fallback takes `env.location.origin` as it is, without the trailing-slash strip. Fix D2's wording to
  match, and update its unit test, adding a case for an `installSh` with a query string. Verify with
  `make web-test`.
- [ ] 7.4 Add one `copyButton(env, text: () => string)` helper that returns the Copy button and its status line.
  Use it in `copyable()`, in the install step and in `projects.ts`'s `linkBlock`. Rename the `mark` local in
  `step()` so it no longer shadows `mark()`. Verify with `make web-test` (the existing copy tests still pass).
- [ ] 7.5 `CliCatalogue` takes Last-Modified from a `FileInfo` read alongside the hash, so a missing file throws
  instead of recording 1601. Verify with `make test`.

## Workflow follow-up

- Whole-change review, then offer `code-review`, then offer `/opsx:archive`.
