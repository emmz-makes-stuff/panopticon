# Spec Delta

## MODIFIED Requirements

### Requirement: Member list
A signed-in member SHALL be able to list the active members who share at least one Project with them, and
themselves, with each one's email, display name, and when they joined. Members who share no Project with them SHALL
NOT be listed.

#### Scenario: List members
- **WHEN** a member opens the members page
- **THEN** every active member who shares a Project with them is listed with email, display name and join date, and
  removed members aren't

#### Scenario: Other Projects' people
- **WHEN** Kit belongs to `PAN`, and Sam belongs only to `ACME`
- **THEN** Kit's members page doesn't list Sam

### Requirement: Removing a member
An initial member (an address in `Panopticon:InitialMembers`) SHALL be able to remove another member from the server,
but not themselves. Nobody else SHALL be able to. Removal SHALL end all of that member's sessions, revoke all of
their API tokens, remove them from every Project, and stop them from signing in. Their past reports stay where they
were.

#### Scenario: Remove a colleague
- **WHEN** an initial member removes `sam@example.com`
- **THEN** Sam's open dashboard sessions stop working, Sam's reporters are refused, and Sam can't request a
  sign-in link

#### Scenario: Can't remove yourself
- **WHEN** an initial member tries to remove themselves
- **THEN** the request is rejected and nothing changes

#### Scenario: Only initial members remove
- **WHEN** a member who isn't an initial member tries to remove someone from the server
- **THEN** it is refused, and nothing changes

#### Scenario: Reports remain
- **WHEN** a removed member's agent had reported on a change
- **THEN** the change's card still shows that agent with the member's display name

#### Scenario: Last owner removed
- **WHEN** an initial member removes Sam, who is the only owner of `ACME`
- **THEN** the removal is refused with a message naming `ACME`, until another member of it is made an owner

## REMOVED Requirements

### Requirement: Members invite members
**Reason**: With Projects, server-wide invites would let anyone add people who then see nothing, and the list of who
can sign in would no longer say who sees what. People now join through a Project (spec projects, "Inviting to a
Project"), which also lets a new address sign in.
**Migration**: Invite people from a Project's page. Members already on the server stay on it, and see work once
they're added to a Project.
