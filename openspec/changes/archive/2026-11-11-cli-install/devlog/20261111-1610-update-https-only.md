# 4.3: `update` only over HTTPS (security review fix)

<!-- Section: 4 · Tasks: 4.3 · Base: c5f466a -->

## What happened

An automated security review of the §4 commit flagged `UpdateCommand` as HIGH risk (insecure update). The manifest,
its SHA-256s and the build all come over one connection. Over plain http, anyone on the path could swap the binary
and its checksum, and `update` would replace itself with their code.

Emmz chose the following (2026-11-11): **https, with loopback exempt**. Signing builds was offered and declined for
now, because it needs a signing key in CI and a public key compiled into the CLI.

## What landed

- **The spec** has a new requirement, "Updates only over HTTPS". It has three scenarios: a plain http server, a plain
  http build URL, and a local server.
- **`UpdateCommand.IsSecure(url)`** checks `https || url.IsLoopback`. `update` checks the server before any request,
  and the build URL before downloading.
- **`design.md` D6** says why, and notes the signing alternative.
- **`UpdateCommandTests`** gets five more cases, 19 in all.

## Not changed

- **`panopticon login` and the bridge still allow plain http servers.** A token over http is a separate, existing
  trade-off, and this fix covers only self-replacement.
- **`install.sh` over http:** the URL is what the person types (`curl -fsSL http://… | sh`), so that's their choice.
  Production is https.

## Gates

The .NET gates passed on the run with this fix: `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` `FORMAT_EXIT:0`
`VALIDATE_EXIT:0`. That run's web gates failed (`WEB_BUILD_EXIT:2` and others) on a syntax slip in my uncommitted §5
edit to `web/src/shell.ts`. That's outside this commit. Once it was fixed, `WEB_BUILD_EXIT:0` `WEB_TEST_EXIT:0`
`WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0`.

## Next

Carry on with §5, task 5.1. Uncommitted so far: `Env.platform` in `web/src/shell.ts` (with `browserPlatform()`) and the
`platform` option in `web/src/testShell.ts`. Still to write: the panel itself (`web/src/views/installCli.ts`), its use
in `tokens.ts`, the tests, CSS, and `web/DESIGN.md`.
