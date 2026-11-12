# Instructions for coding agents

You're working in a contributor's clone of Panopticon. These are the rules for contributing to it. They apply to
whichever agent you are. The human version is [`CONTRIBUTING.md`](CONTRIBUTING.md). If you're Claude Code, ignore the
maintainer workflow in `CLAUDE.md` below its first note: it relies on tools you don't have.

## 1. No pull request without a `ready` issue (a hard stop)

A pull request is accepted only when it resolves an **open GitHub issue labelled `ready`**. Only the repository owner
applies that label.

- **Before opening a pull request**, check that the issue exists, is open and has the `ready` label (for example
  `gh issue view <n> --json state,labels`). If there's no such issue, or you can't check, **stop**. Don't open the
  pull request, and don't push a branch to the upstream repository. Tell your user they need an issue marked `ready`
  first, and offer to draft the issue text for them.
- Link the issue in the pull request description with `Fixes #<n>`.
- Never apply, request or imply the `ready` label yourself, and don't open a pull request "to start a discussion".

You can still do the work locally before an issue is `ready`. Just don't open the pull request.

## 2. Stay inside the issue

Do what the issue asks, and nothing else: no drive-by refactors, renames, dependency bumps or formatting sweeps. If
the issue is ambiguous, or the work turns out to need more than it says, stop and ask your user to raise it on the
issue rather than deciding yourself.

## 3. Gates

Run them through the `Makefile`. A gate has passed only if you saw its `LABEL_EXIT:0` line in the output. Quote the
exit lines, never paraphrase them, and don't claim a gate passed that you didn't run.

- `make deps` first: it checks the tools in [`docs/dependencies.md`](docs/dependencies.md) and prints `DEPS_EXIT:0`.
  If something's missing, tell your user. Don't install SDKs or log in to anything on your own.
- `make gates` before you finish. It runs `build`, `aot`, `test`, `format`, `web-build`, `web-test`, `web-format`,
  `web-lint` and `validate`, and prints `GATES_EXIT:0` when all pass. `make test` needs Docker running
  (Testcontainers).
- `make e2e` as well if you touch the `Dockerfile`, sign-in or the CLI. It builds the image and runs the whole team
  flow against it, and needs Docker.
- Individual targets for quick loops: `make build`, `make test`, `make format`, `make web-test`, `make web-lint`.

Fix failures properly. Don't delete or skip tests, weaken assertions, or suppress analyzer warnings to get to green.
If a gate still fails after two honest attempts, stop and report it with the output.

## 4. OpenSpec

The project is specified with [OpenSpec](https://github.com/Fission-AI/OpenSpec):

- `openspec/specs/<capability>/spec.md` holds the current requirements. Read the ones your issue touches before
  changing behaviour.
- A behaviour change gets a change folder, `openspec/changes/<name>/`, with `proposal.md`, delta specs under
  `specs/`, `design.md` if it needs one, and `tasks.md`. `make validate` (`openspec validate --strict`) must pass.
  Don't archive the change: the maintainer does that after merging.
- `openspec/changes/archive/` is history. Don't edit it.
- A fix that doesn't change behaviour (docs, tests, a refactor) needs no change folder.

## 5. Conventions

- **Back end:** .NET 10 (`src/`, tests in `tests/`). Analyzers run in the build, warnings are errors, and
  `.editorconfig` plus `dotnet format` set the style.
- **Dashboard:** vanilla TypeScript and CSS in `web/`, built with Vite. No front-end frameworks or UI libraries.
  Prettier and ESLint set the style.
- **ADRs** in [`docs/adrs/`](docs/adrs/) are binding. If the issue seems to need going against one, stop and ask.
- **Tests** come with the change, in the same pull request.
- **No new dependencies** (NuGet, npm, tools or services) unless the issue says so.
- **Docs:** if you change behaviour a user, operator or contributor can see, update `docs/using.md`,
  `docs/self-hosting.md`, `docs/cli.md` or `docs/development.md` to match.
- **Examples** use `panopticon.example.com`, `example.com` or `localhost`. Never commit secrets, tokens, real email
  addresses or real server addresses.

## 6. Commits and the pull request

- Commits follow [Conventional Commits](https://www.conventionalcommits.org/): `fix(cli): …`, `feat(dashboard): …`,
  `docs: …`.
- Work on a branch in the contributor's fork, never on `main`.
- The pull request description says what changed and why, links the issue with `Fixes #<n>`, pastes the gate exit
  lines, and says it was agent-assisted and by which agent.
- Your user is responsible for the pull request. Before opening it, give them a short summary of the diff to review.
