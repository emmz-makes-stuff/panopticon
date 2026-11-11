# Whole-change review: the spec is met; six small findings, and fix sections wait on Emmz

<!-- Section: 7 · Tasks: 7.1 done, 7.2 and 7.3 open (they wait for a real release) · Range: 027894b..6df5165 -->

(The machine clock read 12:52 when this was written, behind the earlier notes' times. The file name follows on from
the 19:10 handoff so it sorts as the newest note.)

## What was reviewed

`git diff 027894b..HEAD` (54 files), against `specs/cli-distribution/spec.md`, `design.md ## Decisions` (D1–D9) and
the ADRs. Pre-flight: `DEPS_EXIT:0`, `VALIDATE_EXIT:0`.

Each spec requirement has code and a test: the manifest, downloads, an empty catalogue, `install.sh` (all its
scenarios, musl and checksum included), install location, scripts naming their server, `--version`, self-update
(newer, older, equal, no server), safe self-update, HTTPS-only, the Tokens hint, and the release workflow. Three
things wait on 7.2/7.3 as planned: `install.ps1` (no `pwsh`), the real Release, and Railway's `--path-as-root`. Nothing
drifted between sections. `ServerAddress` replaced the copy of server resolution and `ProblemAsync` in `LoginCommand`,
and `CliVersion` replaced `BridgeHost`'s own version lookup. No scaffolding was left behind.

## Findings

1. **`update` doesn't handle a timeout.** `HttpClient`'s 100 s timeout throws `TaskCanceledException`, which
   `UpdateCommand.RunAsync` doesn't catch, so a server that hangs gives a stack trace instead of a message. The binary
   stays safe. `LoginCommand` already treats this as "couldn't reach". Spec: Safe self-update ("a server that can't be
   reached … a message naming the problem").
2. **A download that breaks part-way is reported as "Can't replace <binary>".** `HttpIOException` is an
   `IOException`, so it lands in the replace catch. The binary is safe, but the message points at the wrong problem.
3. **`install.ps1` doesn't roll back.** If moving the new `.exe` into place fails after the old one was renamed to
   `.old`, the old one isn't moved back, so there's no `panopticon.exe` left. `BinaryReplacer` (D6) does roll back.
   This is drift between the script and the CLI.
4. **`CliCatalogue.Load` can stop start-up.** An I/O error reading a build (for example, permissions) throws from the
   singleton factory at start-up. Spec: "Server without CLI builds … SHALL NOT fail to start". An unreadable build
   should be logged and left out.
5. **Duplicated `FreePort` helpers.** There are three copies: `ContainerEndToEndTests` (from before this change), and
   `PublishedCliUpdateTests` and `InstallShTests` (new). The two in `Panopticon.Cli.IntegrationTests` could share one
   in its `Infrastructure/`, leaving one copy per test project.
6. **Nit:** `panopticon version` (no dashes) works, but `--help` doesn't mention it. Either list it or drop the alias.

## Proposed fix section (not in tasks.md yet: it waits for Emmz's agreement)

`## 8. Review fixes: error paths and drift`
- 8.1 `update`: treat a timeout as "couldn't reach", and report a broken download as a download failure, not a replace
  failure. Add tests for both (findings 1 and 2).
- 8.2 `install.ps1`: move `.old` back if the new `.exe` can't be moved in (finding 3). No automated test, since there's
  no `pwsh`, so it's covered by 7.3.
- 8.3 `CliCatalogue.Load`: log an unreadable build and leave it out, with a test (finding 4).
- 8.4 Tidying: one `FreePort` per test project, and settle the `version` alias (findings 5 and 6).

## Emmz's answers

- All four parts of §8 (8.1–8.4) go in.
- Yes to `code-review` (run at `high` on `change/cli-install`). Its findings get folded into §8, or into a further
  section, for her to agree before anything is built.

## code-review (high) and the agreed fix sections

`code-review` raised ten findings. Four repeat findings 1–4 above. Emmz took **all** of the six new ones:

- **`update` could overwrite the `dotnet` host.** It replaces `Environment.ProcessPath` without the
  `panopticon`-name check the `.old` clean-up uses, so `dotnet …/panopticon.dll update` would rename the AOT build
  over `~/.dotnet/dotnet`. This is the serious one, and it is now 8.1.
- A stalled body hangs forever (`ResponseHeadersRead` means `HttpClient.Timeout` stops applying), and Ctrl+C leaves
  `.panopticon-update-*` behind. Now 8.2.
- There's no early stop once the body is past `size` or `Content-Length`. Now 8.3.
- Downloads have no `Cache-Control`, and the URLs carry no version, so a cache or a rollout overlap could serve stale
  bytes. Emmz picked `no-cache` over versioned paths, which would change the spec. Now 9.2.
- `TryParse` and `TryDelete` are duplicated. Now 8.4.
- The web panel hard-codes the no-builds text instead of showing the Problem `detail`. Now 9.4.

They're written up in `tasks.md` as `## 8.` (update error paths) and `## 9.` (server and install scripts). The
findings above map as 1+2 → 8.2/8.3, 3 → 9.3, 4 → 9.1, and 5+6 → 8.4.

## Next

- **§8, starting with 8.1**: the binary-name guard on `update`. Note the base commit first. Then §9, then hand Emmz
  the release recipe (the 19:10 handoff note) for 7.2 and 7.3.
- (Done: asked Emmz, see above.) Ask Emmz which findings become §8, and offer the `code-review` skill on `change/cli-install`. Add the agreed
  section(s) to `tasks.md` and build them with the section loop, `make gates` included.
- Then hand her the release recipe from the 19:10 handoff note (merge to `main`, push `main` and `v0.2.0`), and run
  7.2, then 7.3. Remind her that local `main` is 2 commits ahead of `origin/main` (the CVE fix), so pushing deploys it
  too.
