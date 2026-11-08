# cli-bridge Specification

## Purpose

The CLI's local stdio MCP server. Any MCP-capable agent can report progress through it, while the bridge
itself supplies the repo identity and the change's documents from the local working copy.

## Requirements

### Requirement: Stdio MCP server
`panopticon mcp` SHALL run an MCP server over stdin/stdout that offers a `report_progress` tool taking
the change name and, optionally, the current section number. It SHALL write nothing but MCP protocol
messages to stdout, and its logs SHALL go to stderr.

#### Scenario: Agent lists tools
- **WHEN** an MCP client starts `panopticon mcp` and lists its tools
- **THEN** the list includes `report_progress` with `change` (required) and `section` (optional)

### Requirement: Configuration
The bridge SHALL read the server URL from `PANOPTICON_SERVER_URL` and the token from `PANOPTICON_TOKEN`.
If either is missing, `report_progress` SHALL return an error naming it, and the bridge SHALL keep
running.

#### Scenario: Missing token
- **WHEN** `PANOPTICON_TOKEN` is unset and the agent calls `report_progress`
- **THEN** the tool returns an error naming `PANOPTICON_TOKEN`, and the bridge keeps serving

### Requirement: Repo root
The bridge SHALL treat the working tree's git top-level directory, found from its working directory, as
the repo root.

#### Scenario: Started in a subdirectory
- **WHEN** the bridge's working directory is `<repo>/src/app`
- **THEN** it reads `openspec/` from `<repo>`

#### Scenario: Not a git repo
- **WHEN** the working directory isn't inside a git working tree
- **THEN** `report_progress` returns an error saying no git repository was found

### Requirement: Repo identity from git remote
The bridge SHALL get the repo identity from the `origin` remote URL, accepting SSH, scp-style and HTTPS
forms from any host. Owner SHALL be every path segment but the last, and repo SHALL be the last segment
without `.git`. Azure DevOps markers (`v3/`, `_git/`) SHALL be dropped.

#### Scenario: GitHub SSH
- **WHEN** origin is `git@github.com:emmz/panopticon.git`
- **THEN** the identity is host `github.com`, owner `emmz`, repo `panopticon`

#### Scenario: GitHub HTTPS
- **WHEN** origin is `https://github.com/emmz/panopticon`
- **THEN** the identity is host `github.com`, owner `emmz`, repo `panopticon`

#### Scenario: GitLab subgroup
- **WHEN** origin is `https://gitlab.com/acme/platform/api.git`
- **THEN** the identity is host `gitlab.com`, owner `acme/platform`, repo `api`

#### Scenario: Azure DevOps HTTPS
- **WHEN** origin is `https://dev.azure.com/acme/Nova/_git/nova-api`
- **THEN** the identity is host `dev.azure.com`, owner `acme/Nova`, repo `nova-api`

#### Scenario: Azure DevOps SSH
- **WHEN** origin is `git@ssh.dev.azure.com:v3/acme/Nova/nova-api`
- **THEN** the identity is host `dev.azure.com`, owner `acme/Nova`, repo `nova-api`

#### Scenario: Credentials in URL
- **WHEN** origin is `https://user:secret@github.com/emmz/panopticon.git`
- **THEN** the identity is host `github.com`, owner `emmz`, repo `panopticon`, and the credentials are
  never sent to the server or logged

#### Scenario: No origin remote
- **WHEN** the repo has no `origin` remote
- **THEN** `report_progress` returns an error saying a remote is required

### Requirement: Change documents read locally
The bridge SHALL read `openspec/changes/<change>/tasks.md` from the repo root, and SHALL refuse change
names that could leave `openspec/changes/`.

#### Scenario: Change exists
- **WHEN** the agent reports change `walking-skeleton` and its `tasks.md` exists
- **THEN** the bridge forwards that file's current content

#### Scenario: Change missing
- **WHEN** the agent reports a change with no folder under `openspec/changes/`
- **THEN** `report_progress` returns an error naming the path it looked for

#### Scenario: Path traversal
- **WHEN** the agent reports change `../../etc`
- **THEN** `report_progress` returns an invalid-name error and reads nothing

### Requirement: Agent identity
The bridge SHALL identify the reporting agent by the MCP client name the agent gave at initialisation,
plus the machine's host name.

#### Scenario: Claude Code on a VM
- **WHEN** Claude Code (client name `claude-code`) runs the bridge on host `vm-1`
- **THEN** reports identify the agent as `claude-code` on `vm-1`

### Requirement: Forwarding to the server
The bridge SHALL forward each report to the server's online MCP endpoint with the configured bearer
token, and SHALL return the server's computed progress to the agent. Server errors SHALL come back to the
agent as tool errors, and the bridge SHALL keep running.

#### Scenario: Successful report
- **WHEN** the server accepts a report
- **THEN** the agent receives a result stating overall and current-section progress

#### Scenario: Server unreachable
- **WHEN** the server can't be reached
- **THEN** the tool returns an error naming the server URL, and the bridge keeps serving later calls

#### Scenario: Token rejected
- **WHEN** the server responds 401
- **THEN** the tool returns an error saying the token was rejected, without echoing the token

### Requirement: Single self-contained binary
The CLI SHALL be published as one self-contained native executable per platform that runs without a .NET
runtime installed.

#### Scenario: Runs on a clean VM
- **WHEN** the published Linux binary is copied to a machine without .NET and runs `panopticon mcp`
- **THEN** the bridge starts and answers an MCP tool listing
