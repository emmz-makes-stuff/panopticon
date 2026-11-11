# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

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
