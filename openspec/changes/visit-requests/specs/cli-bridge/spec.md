## MODIFIED Requirements

### Requirement: Stdio MCP server
`panopticon mcp` SHALL run an MCP server over stdin/stdout that offers a `report_progress` tool taking
the change name and, optionally, the current section number, a `list_projects` tool taking no arguments, a
`tag_change` tool, and a `request_visit` tool. It SHALL write nothing but MCP protocol messages to stdout, and its logs
SHALL go to stderr.

#### Scenario: Agent lists tools
- **WHEN** an MCP client starts `panopticon mcp` and lists its tools
- **THEN** the list includes `report_progress` with `change` (required) and `section` (optional),
  `list_projects`, `tag_change`, and `request_visit` with `url` and `reason` (required) and `action` (optional)

## ADDED Requirements

### Requirement: Requesting a visit
The bridge's `request_visit` SHALL take a `url`, a `reason` and an optional `action`, and SHALL return an error naming
the problem when they break the visit limits (see `remote-answers`). With the dashboard's answer, it SHALL return the
outcome, the note and who answered. Without one, it SHALL return at once, telling the agent to ask in the terminal with
the URL in the question. It SHALL NOT itself wait or contact the server.

#### Scenario: Answered
- **WHEN** Emmz answered a held visit request Done with "approved"
- **THEN** `request_visit` returns "Done: approved. Answered by Emmz via Panopticon.", with the outcome, note and
  answerer as structured content too

#### Scenario: Not held
- **WHEN** `request_visit` runs and the dashboard gave no answer (the member isn't away, the request was released, or
  the hook isn't installed)
- **THEN** it returns at once, telling the agent to ask in the terminal with the URL in the question

### Requirement: Visit tool instructions
The description of `request_visit` SHALL tell the agent to use it whenever it needs its member to look at or act on a
web page, such as reviewing a pull request, approving a run or checking how a page looks. It SHALL also tell the agent
to wait for the answer before relying on the visit.

#### Scenario: Agent reads the tool
- **WHEN** an MCP client lists the bridge's tools
- **THEN** `request_visit`'s description names reviewing a pull request, approving a run and checking a page as uses
