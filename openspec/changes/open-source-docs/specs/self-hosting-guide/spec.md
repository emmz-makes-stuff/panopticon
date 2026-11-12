# Spec Delta

## Purpose

Promises anyone who wants their own Panopticon server a complete, accurate guide in the repository, so that standing
one up needs nothing beyond the repo and the accounts it names.

## ADDED Requirements

### Requirement: Complete self-hosting guide
The repository SHALL have `docs/self-hosting.md`, linked from the README's opening, covering everything needed to run
a production server: what it needs, building the image, configuration, migrations, the first member, CLI builds,
deployment, upgrading and backups.

#### Scenario: Standing up a server from the guide
- **WHEN** someone with a container host, Postgres, an S3-compatible bucket, a Resend account and a domain follows
  `docs/self-hosting.md` from the top
- **THEN** they reach a server that signs its first member in by email and accepts a `panopticon login` from a
  machine, without needing any other document

#### Scenario: Every setting is documented
- **WHEN** someone compares the guide's configuration table with the settings the server reads
- **THEN** every setting the server requires outside Development appears, with its meaning and whether it's required

### Requirement: Production safety stated up front
The self-hosting guide SHALL state, before its first deployment step, that a production server must not run as
Development, and why.

#### Scenario: Reader skims to the commands
- **WHEN** someone reads the guide only as far as its first deployment step
- **THEN** they have already been told that Development logs sign-in links and accepts a public bootstrap token

### Requirement: Distributing the CLI from your server
The self-hosting guide SHALL explain how a self-hosted server gets the CLI builds it hands out, and what its members
see when it has none.

#### Scenario: Server without CLI builds
- **WHEN** someone deploys an image built from a plain checkout
- **THEN** the guide has told them it has no CLI builds, what the setup guide then shows, and the ways to include them

### Requirement: Releases from a fork
The self-hosting guide SHALL explain what the repository's tag-triggered release workflow does in a fork, and what it
needs before it can deploy.

#### Scenario: Fork pushes a version tag
- **WHEN** someone who forked the repository pushes a `v*` tag
- **THEN** the guide has told them which jobs run, that the deploy needs their own Railway project and `RAILWAY_TOKEN`
  secret, and how to deploy without it

### Requirement: No dependency on a public server
Outside `openspec/changes/archive/`, the repository's files SHALL NOT name any public Panopticon server; examples
SHALL use `panopticon.example.com` or `localhost`.

#### Scenario: Searching the tree for a server domain
- **WHEN** someone searches the working tree, excluding `openspec/changes/archive/`, for `panopticon.team`
- **THEN** nothing is found
