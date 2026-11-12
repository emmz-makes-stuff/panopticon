# Contributing to Panopticon

Thanks for wanting to help. Bug reports, ideas and code are all welcome, and so are contributions written with a coding
agent. There's one rule that matters more than the rest, so it comes first.

## Pull requests are only for `ready` issues

Every pull request has to resolve an open issue that carries the **`ready`** label. Only the repository owner applies
that label, once they've agreed the issue should be done and roughly how.

1. **Open an issue first** (or find an existing one) for the bug or the idea. Say what you'd like to change and why.
2. **Wait for `ready`.** The owner may ask questions, change the scope, or decide it isn't for Panopticon. That's a
   much cheaper conversation to have before anyone writes code.
3. **Open the pull request** once the issue is `ready`, and link it with `Fixes #123` in the description.

A pull request that doesn't link an open `ready` issue is closed without review, however good it is. That isn't
personal. If you've already written something, open an issue describing it and ask there.

Typo and broken-link fixes still need an issue, but the owner will usually mark those `ready` straight away.

## Contributions written with agents

Agent-written contributions are welcome, on exactly the same terms as any other:

- the `ready` rule above applies unchanged;
- the gates must pass (below);
- **you're responsible for what you submit.** Read and understand the whole diff before opening the pull request, and
  be ready to answer questions about it yourself.

Point your agent at [`AGENTS.md`](AGENTS.md), which has these rules written for it, including when to stop. Please
say in the pull request that it was agent-assisted, and which agent you used.

## Setting up

[`docs/development.md`](docs/development.md) covers the tools you need (`make deps` checks them), running the server
and dashboard locally, and the gates. In short:

- `make gates` runs everything: the .NET build (analyzers are errors), the tests, formatting, the CLI's Native AOT
  publish, the web build, tests, formatting and lint, and OpenSpec validation. `make test` needs Docker.
- `make e2e` runs the whole team flow against the built image. Run it as well if you touch the `Dockerfile`, sign-in,
  or the CLI.

A pull request needs `make gates` green. Paste its exit lines (`GATES_EXIT:0` and the rest) in the description.

## How changes are made here

Panopticon is built with [OpenSpec](https://github.com/Fission-AI/OpenSpec). Anything that changes behaviour gets a
change folder under `openspec/changes/<name>/` (a proposal, specs, a design if it needs one, and tasks), and
`make validate` checks it. The requirements live in `openspec/specs/`. The architecture decisions in `docs/adrs/` are
binding: a pull request that goes against one needs the ADR discussed on the issue first. Small fixes that don't change
behaviour (docs, a test, a refactor) don't need a change folder.

Some house conventions:

- **The back end** is .NET 10, and **the dashboard** is vanilla TypeScript and CSS built with Vite, with no front-end
  framework ([ADR-0002](docs/adrs/ADR-0002-dotnet-10-and-vanilla-typescript.md)).
- **Formatting** follows `.editorconfig`, `dotnet format` and Prettier. `make format` and `make web-format` check it.
- **Tests come with the change**, in the same pull request.
- **Commits** follow [Conventional Commits](https://www.conventionalcommits.org/), as the history does:
  `fix(cli): …`, `feat(dashboard): …`, `docs: …`.
- **Keep pull requests small**, and about one issue each.
- **Never commit secrets** or real server addresses. Examples use `panopticon.example.com`, `example.com` and
  `localhost`.

## Licence

Panopticon is MIT-licensed ([`LICENSE`](LICENSE)). By contributing, you agree that your contribution is released under
the same licence. There's no CLA.

## Conduct and security

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md). Please **don't** report security
vulnerabilities in issues. Follow [`SECURITY.md`](SECURITY.md) to report them privately.
