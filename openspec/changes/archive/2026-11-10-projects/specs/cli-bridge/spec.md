# Spec Delta

## MODIFIED Requirements

### Requirement: Stdio MCP server
`panopticon mcp` SHALL run an MCP server over stdin/stdout that offers a `report_progress` tool taking
the change name and, optionally, the current section number, and a `list_projects` tool taking no arguments. It
SHALL write nothing but MCP protocol messages to stdout, and its logs SHALL go to stderr.

#### Scenario: Agent lists tools
- **WHEN** an MCP client starts `panopticon mcp` and lists its tools
- **THEN** the list includes `report_progress` with `change` (required) and `section` (optional), and
  `list_projects`

## ADDED Requirements

### Requirement: Project link file
The bridge SHALL read `.panopticon.json` at the repo root on each report. The file is a JSON object whose `project`
field is a Project id, e.g. `{ "project": "6f1c…" }`. When it's there, the report SHALL carry that id. With no file,
the report SHALL carry no Project. Other fields SHALL be ignored, so later changes can add to the file.

#### Scenario: Linked repo
- **WHEN** the repo root holds `.panopticon.json` naming `PAN`'s id, and the agent calls `report_progress`
- **THEN** the report carries `PAN`'s id

#### Scenario: No link file
- **WHEN** the repo root has no `.panopticon.json`
- **THEN** the report carries no Project, and it's accepted into the reporter's personal scope

### Requirement: Malformed link file
When `.panopticon.json` isn't valid JSON, or its `project` is missing or not a UUID, `report_progress` SHALL return
an error naming the file and what's wrong with it, SHALL send nothing, and the bridge SHALL keep running. It SHALL
NOT fall back to an unlinked report, so a typo can't hide the work in a personal scope.

#### Scenario: Not a UUID
- **WHEN** `.panopticon.json` is `{ "project": "PAN" }`
- **THEN** the tool returns an error naming `.panopticon.json` and saying `project` must be a Project id from
  `list_projects`, and nothing is sent

### Requirement: Listing Projects through the bridge
The bridge's `list_projects` SHALL forward to the server's `list_projects` with the same server and token as reports
(see Configuration), and return its result. A missing token, an unreachable server or a refusal SHALL be a tool error
saying what went wrong, as for `report_progress`, and the bridge SHALL keep running.

#### Scenario: Agent links a repo
- **WHEN** an agent calls the bridge's `list_projects`
- **THEN** it gets the reporter's Projects with their ids, names, codes and roles, from which it can write
  `.panopticon.json`

#### Scenario: No login
- **WHEN** no token is configured and the agent calls `list_projects`
- **THEN** the tool returns an error naming `PANOPTICON_TOKEN` and `panopticon login`
