# machine-setup Specification

## Purpose

The signed-in guide to setting up a machine so its agent reports to this server: install the CLI, log in, and connect
an agent. Each step shows as done once the member's own tokens show it.

## Requirements

### Requirement: Three setup steps
The setup guide SHALL show three numbered steps in order. First, install the CLI. Second, log in with
`panopticon login --server <server>`, where `<server>` is this server's public URL. Third, connect your agent. It SHALL
say that the steps run on the machine where the agent works, and it SHALL NOT cover linking a repo to a Project.

#### Scenario: Steps on the public server
- **WHEN** a member opens the guide on `https://panopticon.example.com`
- **THEN** it shows Install the CLI, Log in with `panopticon login --server https://panopticon.example.com`, and Connect your
  agent, in that order

#### Scenario: Self-hosted server
- **WHEN** the server's public URL is `https://panopticon.example.com`
- **THEN** the log-in step's command is `panopticon login --server https://panopticon.example.com`

### Requirement: Install step
The install step SHALL show the install command for the visitor's OS, with a copy action and the other OS's command
one click away, as cli-distribution's install hint describes. On a server without CLI builds, it SHALL say so and point
to building from source, and the other two steps SHALL still show.

#### Scenario: Server without CLI builds
- **WHEN** the server has no CLI builds
- **THEN** the install step says this server has no CLI builds and to build from source, and the log-in and connect
  steps still show

### Requirement: Connect step for Claude Code
The connect step SHALL lead with the Claude Code plugin. It SHALL show these commands to run in Claude Code, each one
copyable:
- `/plugin marketplace add emmz-makes-stuff/panopticon-skills`
- `/plugin install panopticon@panopticon-skills`
- `/panopticon:setup`

It SHALL say that the plugin uses the machine's saved login, so the commands are the same for every server.

#### Scenario: Plugin commands
- **WHEN** a member reads the connect step
- **THEN** it shows the marketplace, install and `/panopticon:setup` commands, each with a way to copy it

### Requirement: Connect step for other agents
The connect step SHALL also offer, behind one click, the settings for any MCP client: a stdio server whose command is
`panopticon` with the arguments `mcp`, started in the repo, as `.mcp.json` content that can be copied.

#### Scenario: Another agent
- **WHEN** a member opens the other-agents option
- **THEN** it shows `.mcp.json` content with a `panopticon` stdio server running `panopticon mcp`, with a way to copy
  it

### Requirement: Steps marked done
The guide SHALL mark steps done from the member's own active tokens:
- once they have any token, the install and log-in steps;
- once any of their tokens has been used by an agent (a report, or any other call to the server, as its "last used"
  time shows), the connect step too.

A step marked done SHALL stay readable, so another machine can be set up from it.

#### Scenario: No tokens
- **WHEN** a member with no tokens opens the guide
- **THEN** no step is marked done

#### Scenario: Logged in, not yet used
- **WHEN** a member's only token has never been used
- **THEN** install and log-in are marked done, and connect is not

#### Scenario: Used
- **WHEN** one of a member's tokens has been used
- **THEN** all three steps are marked done, and each one's commands still show

### Requirement: Guide on the Tokens page
The Tokens page SHALL always show the setup guide, whatever the member's tokens.

#### Scenario: Member with used tokens
- **WHEN** a member whose tokens have all been used opens the Tokens page
- **THEN** the guide shows there, with every step marked done

### Requirement: Guide on the Projects page
The Projects page SHALL show the setup guide while none of the member's tokens has been used. It SHALL offer a way to
hide it, which this browser remembers, and SHALL NOT show the guide once a token has been used.

#### Scenario: New member
- **WHEN** a member with no tokens opens the Projects page
- **THEN** the setup guide shows there

#### Scenario: Hidden
- **WHEN** a member hides the guide on the Projects page and later opens it again in the same browser
- **THEN** the guide doesn't show there, and still shows on the Tokens page

#### Scenario: Member already set up
- **WHEN** one of a member's tokens has been used
- **THEN** the Projects page doesn't show the guide
