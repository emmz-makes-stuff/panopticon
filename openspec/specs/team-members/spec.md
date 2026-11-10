# team-members Specification

## Purpose

Who belongs to the team that can use a Panopticon deployment: how the first members are set up, how members
invite and remove each other, and what a member's removal takes away.

## Requirements

### Requirement: Members are identified by email
A member SHALL be identified by an email address. Matching is case-insensitive and ignores surrounding
whitespace. An address SHALL belong to at most one member.

#### Scenario: Case-insensitive email
- **WHEN** `Emmz@Example.com` is a member and someone asks to sign in as `emmz@example.com `
- **THEN** it is treated as the same member

### Requirement: Initial members from configuration
The server SHALL add each address in `Panopticon:InitialMembers` as a member at start-up, unless that address
already has a member record, removed or not. It SHALL NOT start when there's no active member and no initial
member is configured.

#### Scenario: First start
- **WHEN** the server starts with no members and `Panopticon:InitialMembers` lists `emmz@example.com`
- **THEN** `emmz@example.com` is a member and can sign in

#### Scenario: Removed initial member stays removed
- **WHEN** an initial member was removed and the server restarts with the same configuration
- **THEN** that address is still not a member

#### Scenario: Nobody can sign in
- **WHEN** the server starts with no active member and no initial member configured
- **THEN** start-up fails, naming `Panopticon:InitialMembers`

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

### Requirement: Display names
A member's display name SHALL default to the part of the email before `@`. Each member SHALL be able to change
their own display name, from 1 to 50 characters after trimming. Display names SHALL be shown as text, never as
markup.

#### Scenario: Default name
- **WHEN** `emmz@example.com` is invited without a display name
- **THEN** that member's display name is `emmz`

#### Scenario: Rename yourself
- **WHEN** a member changes their own display name to `Emmz`
- **THEN** the member list and that member's agents on cards show `Emmz`

#### Scenario: Blank name rejected
- **WHEN** a member tries to set their display name to spaces only
- **THEN** the change is rejected and the name stays as it was

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
