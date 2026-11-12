# Spec Delta

## MODIFIED Requirements

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

