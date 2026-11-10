## MODIFIED Requirements

### Requirement: Membership roles
Each member of a Project SHALL have one role:
- `owner`: manages the Project's members and name, and everything an editor can do;
- `editor`: everything a viewer can do, plus managing the Project's tags and tagging its changes (see `tags`);
- `viewer`: sees the Project's work and reports into it.

A person SHALL have at most one role in a Project.

#### Scenario: Roles listed
- **WHEN** a member opens a Project's page
- **THEN** each member is listed with their display name, email and role

#### Scenario: An editor manages tags
- **WHEN** an editor of `PAN` opens `PAN`'s page
- **THEN** they can add, rename and delete `PAN`'s tags, and can't invite or remove members

### Requirement: Project page
A Project's page SHALL show its name, code and id, with a way to copy the `.panopticon.json` that links a repo to it,
its members, and its tags. Owners SHALL be able to rename it and manage its members there. Owners and editors SHALL be
able to manage its tags there.

#### Scenario: Linking text
- **WHEN** a member opens `PAN`'s page
- **THEN** it shows `PAN`'s id, and the `.panopticon.json` content naming that id, ready to copy

#### Scenario: Tags section
- **WHEN** a viewer opens `PAN`'s page
- **THEN** it lists `PAN`'s tags, with no way to change them

### Requirement: Listing Projects over MCP
The server's MCP endpoint and the bridge SHALL offer a `list_projects` tool. It SHALL return the Projects the
reporter's member belongs to, each with its id, name, code, the member's role, and its tags. A bootstrap-token reporter
(Development) SHALL get every Project.

#### Scenario: An agent finds the Project to link
- **WHEN** an agent with Emmz's token calls `list_projects`
- **THEN** it gets each of Emmz's Projects with its id, name, code and her role, and no others

#### Scenario: An agent sees the tags
- **WHEN** an agent calls `list_projects`, and `PAN` has `bug` and `area: onboarding`
- **THEN** `PAN`'s entry lists `bug` and `area: onboarding`
