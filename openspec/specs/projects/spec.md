# projects Specification

## Purpose
Projects group work and people on one server: a Project has an id, a name and a code, and its members, each with a
role, are the people who see and report its work.

## Requirements

### Requirement: Creating a Project
Any signed-in member SHALL be able to create a Project with a name and a code. The server SHALL give it a new random
UUID as its id, and SHALL make its creator its owner.

#### Scenario: Create a Project
- **WHEN** Emmz creates a Project named `Panopticon` with code `PAN`
- **THEN** the Project exists with a new id, and Emmz is its owner

### Requirement: Project codes
A code SHALL be 2 to 10 letters or digits, starting with a letter, and SHALL be stored in capitals. A code SHALL be
unique on the server, without regard to case, and SHALL NOT change once the Project is created.

#### Scenario: Code taken
- **WHEN** a Project with code `PAN` exists and someone creates another with code `pan`
- **THEN** it is rejected with a message saying the code is taken, and nothing is created

#### Scenario: Invalid code
- **WHEN** someone creates a Project with code `P-1`
- **THEN** it is rejected with a message saying what a code may contain

#### Scenario: Code fixed
- **WHEN** an owner edits Project `PAN`
- **THEN** the name can be changed, and the code cannot

### Requirement: Project names
A name SHALL be 1 to 100 characters after trimming. An owner SHALL be able to rename the Project. Names need not be
unique.

#### Scenario: Rename a Project
- **WHEN** an owner renames `Panopticon` to `Panopticon server`
- **THEN** every member sees the new name, and the id and code are unchanged

#### Scenario: Blank name
- **WHEN** someone creates or renames a Project with a blank name
- **THEN** it is rejected with a message saying a name is required

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

### Requirement: Inviting to a Project
An owner SHALL be able to invite an email address to their Project, with a role. If the address isn't an active
member of the server, the invite SHALL also make it one (making a removed member active again), so it can sign in. No
invitation email is sent.

#### Scenario: Invite someone new
- **WHEN** an owner of `PAN` invites `sam@example.com` as a viewer, and Sam has never signed in
- **THEN** Sam is a viewer of `PAN` and can request a sign-in link

#### Scenario: Invite someone already on the server
- **WHEN** an owner of `PAN` invites `kit@example.com`, who is already a member of the server
- **THEN** Kit becomes a member of `PAN` with that role

#### Scenario: Already in the Project
- **WHEN** an owner invites an address that is already a member of the Project
- **THEN** it is rejected with a message saying so, and nothing changes

#### Scenario: Invalid address
- **WHEN** an owner invites `not-an-email`
- **THEN** it is rejected with a message saying the address isn't valid

#### Scenario: Only owners invite
- **WHEN** an editor or a viewer tries to invite someone
- **THEN** it is refused, and nothing changes

### Requirement: Changing roles and removing
An owner SHALL be able to change any member's role and remove any member from the Project. Any member SHALL be able
to leave a Project. A Project SHALL always have at least one owner, so the last owner SHALL NOT be demoted, removed
or leave.

#### Scenario: Make a second owner
- **WHEN** an owner of `PAN` changes Kit's role to owner
- **THEN** `PAN` has two owners

#### Scenario: Last owner stays
- **WHEN** the only owner of `PAN` tries to leave, or to make themselves an editor
- **THEN** it is refused with a message saying a Project needs an owner, and nothing changes

#### Scenario: Removed from a Project
- **WHEN** an owner removes Sam from `PAN`
- **THEN** Sam no longer sees `PAN` or its work, and Sam's later reports into `PAN` are refused

### Requirement: Projects list
A signed-in member SHALL be able to list the Projects they belong to, each with its name, code and their role, and
SHALL NOT see Projects they don't belong to. Opening a Project they don't belong to SHALL answer as though it didn't
exist.

#### Scenario: Only your Projects
- **WHEN** Sam belongs to `PAN` only, and Projects `PAN`, `ACME` and `HOME` exist
- **THEN** Sam's Projects list shows `PAN`, and opening `ACME`'s page says it wasn't found

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

### Requirement: Asking an agent to link a repo
Next to the `.panopticon.json` to copy, a Project's page SHALL say that an agent connected to Panopticon can link the
repo itself: ask it to link this repo to the Project, by the Project's code. A member who can't copy the file then
still has a way to link a repo.

#### Scenario: Link hint
- **WHEN** a member opens `PAN`'s page
- **THEN** next to the `.panopticon.json` to copy, it says the member can ask their agent to link the repo to `PAN`
