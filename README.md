<p align="center"><img src="docs/images/logo.png" width="128" alt=""></p>

# PANOPTICON

**Every coding agent, every OpenSpec change, one live dashboard.**

You've got Claude Code working through a change in one repo, another agent halfway through a second, and a teammate's
agents somewhere else entirely. Each one is ticking off tasks in its own `tasks.md`, in its own terminal, and the only
way to know where any of them has got to is to go and look. Panopticon looks for you.

Agents report their progress as they work through [OpenSpec](https://github.com/Fission-AI/OpenSpec) changes, and the
dashboard shows one card per change, filling in section by section as tasks are ticked, live.

- **See it all at once.** One card per active change, with whole-change and section progress, who's running it and
  whether its agent is busy, idle or gone quiet.
- **Read the change, not just the count.** Each card opens the change's own page: the proposal, design, tasks, specs
  and devlog, rendered and kept up to date while the agent works.
- **Answer from anywhere.** When an agent stops to ask a question or wait for a permission, it can show up on the
  dashboard, and you can answer it there.
- **Know how long things really take.** Archived changes move to the history, with how long each one and each of its
  sections took, and a metrics page for the slow bits.
- **Built for teams.** Projects group repos and people, with owners, editors and viewers, and tags to slice the work
  however you like. Personal work stays yours.
- **Nothing to host where the agent works.** A small CLI runs as a local MCP bridge: install it, `panopticon login`,
  add the plugin, done.

## How it works

Your agent talks to `panopticon mcp`, a local MCP server. When it starts, resumes or ticks tasks in a change, it calls
`report_progress`, and the bridge sends the whole change folder to your Panopticon server. The server parses
`tasks.md`, stores the documents, and pushes the update to every open dashboard. When `/opsx:archive` moves the
change into the archive, the bridge notices and the change moves to the history.

Members sign in with a link sent by email, and each machine that reports gets its own API token through
`panopticon login`.

## Getting on a server

Panopticon servers grow by invitation: someone already on a server invites you by email address, and you sign in
with the link they send. If you know someone running one, ask them.

Or run your own. A server is one container, Postgres and an S3-compatible bucket, and
[`docs/self-hosting.md`](docs/self-hosting.md) takes you from a checkout to a server your team can sign in to.

## Documentation

- [Using Panopticon](docs/using.md): signing in, Projects, tags, reporting progress, change pages, history and metrics.
- [The `panopticon` CLI](docs/cli.md): installing it, logging in, connecting Claude Code and other agents.
- [Hosting your own server](docs/self-hosting.md): configuration, deployment, CLI builds, upgrades and backups.
- [Development](docs/development.md): running it locally, working on the dashboard, and the gates.
- Background: requirements in [`docs/DISCOVERY.md`](docs/DISCOVERY.md), decisions in [`docs/adrs/`](docs/adrs/), and
  the dashboard's design in [`web/DESIGN.md`](web/DESIGN.md).

## Contributing

Contributions are welcome, including ones written with a coding agent. Start with an issue: pull requests are only
accepted for issues the repository owner has labelled `ready`. [`CONTRIBUTING.md`](CONTRIBUTING.md) has the details, and
[`AGENTS.md`](AGENTS.md) has the same rules for your agent.

Please read the [code of conduct](CODE_OF_CONDUCT.md), and report security issues privately as described in
[`SECURITY.md`](SECURITY.md).

## Licence

Panopticon is released under the [MIT licence](LICENSE). Copyright (c) 2026 Emmz.
