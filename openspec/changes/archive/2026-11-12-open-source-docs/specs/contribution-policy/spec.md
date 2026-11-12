# Spec Delta

## Purpose

Sets out the terms on which the open-source repository is shared and how people, and the coding agents they use,
contribute to it: the licence, the issue-first rule for pull requests, conduct, and private security reporting.

## ADDED Requirements

### Requirement: MIT licence
The repository SHALL carry the MIT licence in a `LICENSE` file at its root, with the copyright line
`Copyright (c) 2026 Emmz`, and the README SHALL name the licence and link to that file.

#### Scenario: Licence is detectable
- **WHEN** someone opens the repository on GitHub
- **THEN** GitHub identifies the licence as MIT from `LICENSE`, and the file's copyright line reads
  `Copyright (c) 2026 Emmz`

### Requirement: Pull requests only for ready issues
`CONTRIBUTING.md` SHALL state that a pull request is accepted only when it resolves an open issue carrying the `ready`
label, that only the repository owner(s) apply that label, and that a pull request without one is closed unreviewed.

#### Scenario: Contributor with an idea
- **WHEN** a contributor reads `CONTRIBUTING.md` wanting to add a feature
- **THEN** it tells them to open (or find) an issue first, wait for the `ready` label, and link that issue from the
  pull request

#### Scenario: Pull request without a ready issue
- **WHEN** a contributor reads what happens to a pull request that links no `ready` issue
- **THEN** `CONTRIBUTING.md` says it will be closed without review, and that they can ask on the issue instead

### Requirement: Agent-generated contributions
`CONTRIBUTING.md` SHALL welcome contributions written with coding agents under the same rules as any other, and SHALL
hold the human who opens the pull request responsible for it.

#### Scenario: Contributor using an agent
- **WHEN** a contributor plans to have an agent write their change
- **THEN** `CONTRIBUTING.md` says that is welcome, that the `ready` rule and the gates apply unchanged, and that they
  must have read and understood what they submit

### Requirement: Instructions for contributing agents
The repository SHALL have an `AGENTS.md` at its root giving a coding agent the contribution rules, the gate commands
and the OpenSpec layout, and SHALL make it an explicit stop for the agent to open a pull request without a `ready`
issue.

#### Scenario: Agent asked to open a pull request
- **WHEN** a contributor's agent reads `AGENTS.md` and is asked to open a pull request for work with no linked issue
  labelled `ready`
- **THEN** the instructions tell it not to open the pull request, and to tell its user to get an issue marked `ready`
  first

#### Scenario: Claude Code contributor
- **WHEN** Claude Code starts in a contributor's clone and loads `CLAUDE.md`
- **THEN** the first thing in `CLAUDE.md` tells anyone who isn't the maintainer to follow `AGENTS.md` instead of the
  maintainer's workflow below

### Requirement: Private security reports
The repository SHALL have a `SECURITY.md` telling people to report vulnerabilities privately through GitHub's private
vulnerability reporting and not in a public issue, discussion or pull request.

#### Scenario: Someone finds a vulnerability
- **WHEN** someone finds a way to read another member's changes
- **THEN** `SECURITY.md` sends them to the repository's Security tab to report it privately, and says which versions
  are supported

### Requirement: Code of conduct
The repository SHALL have a `CODE_OF_CONDUCT.md` adopting the Contributor Covenant 2.1, whose reporting route uses
GitHub only and publishes no email address.

#### Scenario: Someone needs to report conduct
- **WHEN** someone reads `CODE_OF_CONDUCT.md` to report behaviour privately
- **THEN** it gives them a GitHub-only route to the repository owner(s), and no email address appears in the file
