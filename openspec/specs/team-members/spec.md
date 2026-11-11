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
NOT be listed, except to an initial member, who SHALL be able to list every active member of the server.

#### Scenario: List members
- **WHEN** a member opens the members page
- **THEN** every active member who shares a Project with them is listed with email, display name and join date, and
  removed members aren't

#### Scenario: Other Projects' people
- **WHEN** Kit belongs to `PAN`, and Sam belongs only to `ACME`
- **THEN** Kit's members page doesn't list Sam

#### Scenario: An initial member sees everyone
- **WHEN** Emmz is an initial member, and Kit was invited to the server and belongs to no Project
- **THEN** Emmz's members page lists Kit, with a Remove button

#### Scenario: Invitees don't see each other
- **WHEN** Kit and Lou were both invited to the server and share no Project
- **THEN** Kit's members page lists only Kit

### Requirement: Inviting to the server
An initial member SHALL be able to invite an email address to the server from the Members page. When the address
isn't a member, the invite SHALL make it an active member. When it belongs to a removed member, it SHALL make them
active again. The invite SHALL add them to no Project. Nobody else SHALL be able to invite to the server, and the
Members page SHALL offer the invite only to initial members.

#### Scenario: Inviting a friend
- **WHEN** Emmz, an initial member, invites `kit@example.com`, which isn't a member
- **THEN** Kit is an active member with no Projects, and can sign in

#### Scenario: Inviting back
- **WHEN** Emmz invites the address of a member she removed
- **THEN** they are active again, with no Projects and no tokens

#### Scenario: Not an initial member
- **WHEN** Sam, who isn't an initial member, opens the Members page or asks the server to invite an address
- **THEN** the page shows no invite, and the server refuses with a 403, adding nobody and sending nothing

#### Scenario: Invalid address
- **WHEN** Emmz invites `not-an-address`
- **THEN** the invite is refused, naming the problem, and nobody is added

### Requirement: Inviting an existing member
An invite to the address of an active member SHALL be refused with a message saying they're already a member. It
SHALL change nothing and SHALL send no email.

#### Scenario: Already a member
- **WHEN** Emmz invites `Kit@Example.com`, and `kit@example.com` is an active member
- **THEN** the invite is refused with "already a member", and no email is sent

### Requirement: Invitation email
An accepted server invite SHALL email the invited address through the sign-in email sender (see
`dashboard-sign-in`), naming who invited them, with a link to the sign-in page on `Panopticon:PublicUrl`. The email
SHALL NOT contain a sign-in link or any other secret. The inviter's display name SHALL appear as text, never as
markup.

#### Scenario: Email sent
- **WHEN** Emmz invites `kit@example.com`
- **THEN** Kit gets an email saying Emmz invited them to Panopticon, with a link to the server's sign-in page

#### Scenario: Markup in a name
- **WHEN** the inviter's display name is `<b>Emmz</b>`
- **THEN** the email shows `<b>Emmz</b>` as text

#### Scenario: Development without Resend
- **WHEN** the server runs in Development with no Resend key and an invite is accepted
- **THEN** the email is logged instead of sent

### Requirement: Invite rate limit
The server SHALL limit server invites per inviting member. An invite over the limit SHALL be refused with a message to
try again later, SHALL add nobody, and SHALL send no email.

#### Scenario: Too many invites
- **WHEN** an initial member goes over the invite limit
- **THEN** the next invite is refused with a message to try again later, and no email is sent

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
