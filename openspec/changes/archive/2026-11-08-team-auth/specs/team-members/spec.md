# Spec Delta

## Purpose

Who belongs to the team that can use a Panopticon deployment: how the first members are set up, how members
invite and remove each other, and what a member's removal takes away.

## ADDED Requirements

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

### Requirement: Members invite members
Any signed-in member SHALL be able to invite an email address, with an optional display name. That makes it a
member at once, or makes a removed member active again. The invited person then signs in like any member. No
invitation email is sent.

#### Scenario: Invite a colleague
- **WHEN** a member invites `sam@example.com`
- **THEN** `sam@example.com` appears in the member list and can request a sign-in link

#### Scenario: Invalid address
- **WHEN** a member invites `not-an-email`
- **THEN** the invitation is rejected with a message saying the address isn't valid, and nobody is added

#### Scenario: Already a member
- **WHEN** a member invites an address that is already an active member
- **THEN** the invitation is rejected with a message saying so, and nothing changes

### Requirement: Member list
Any signed-in member SHALL be able to list the active members, with each one's email, display name, and when
they joined.

#### Scenario: List members
- **WHEN** a member opens the members page
- **THEN** every active member is listed with email, display name and join date, and removed members aren't

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
Any signed-in member SHALL be able to remove another member, but not themselves. Removal SHALL end all of that
member's sessions, revoke all of their API tokens, and stop them from signing in. Their past reports stay on
the dashboard.

#### Scenario: Remove a colleague
- **WHEN** a member removes `sam@example.com`
- **THEN** Sam's open dashboard sessions stop working, Sam's reporters are refused, and Sam can't request a
  sign-in link

#### Scenario: Can't remove yourself
- **WHEN** a member tries to remove themselves
- **THEN** the request is rejected and nothing changes

#### Scenario: Reports remain
- **WHEN** a removed member's agent had reported on a change
- **THEN** the change's card still shows that agent with the member's display name
