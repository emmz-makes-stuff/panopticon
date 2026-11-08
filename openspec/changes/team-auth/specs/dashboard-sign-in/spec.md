# Spec Delta

## Purpose

How a team member signs in to the dashboard with a magic link sent by email, how their session lasts and
ends, and the rule that the dashboard and its APIs are only for signed-in members.

## ADDED Requirements

### Requirement: Requesting a sign-in link
Anyone SHALL be able to ask for a sign-in link by entering an email address. When the address belongs to an
active member, the server SHALL email them a link. The response SHALL be the same whether or not it does, so
the page doesn't reveal who is a member.

#### Scenario: Member asks for a link
- **WHEN** member `emmz@example.com` asks for a sign-in link
- **THEN** an email with a sign-in link is sent to `emmz@example.com`, and the page says to check their email

#### Scenario: Stranger asks for a link
- **WHEN** someone asks for a link for `stranger@example.com`, which isn't a member
- **THEN** no email is sent, and the page shows exactly what it shows a member

### Requirement: Sign-in links
A sign-in link SHALL be usable once and SHALL expire 15 minutes after it is sent. Only a hash of its secret
SHALL be stored. A newer link SHALL NOT invalidate older unexpired ones.

#### Scenario: Link used twice
- **WHEN** a member signs in with a link, then uses the same link again
- **THEN** the second use is refused, saying the link has already been used, and offers to send a new one

#### Scenario: Expired link
- **WHEN** a member uses a link 16 minutes after it was sent
- **THEN** sign-in is refused, saying the link has expired, and offers to send a new one

#### Scenario: Link for a removed member
- **WHEN** a link is used after its member was removed
- **THEN** sign-in is refused

### Requirement: Confirming before signing in
Opening a sign-in link SHALL NOT sign the member in by itself. The page SHALL ask the member to confirm, and
only that confirmation SHALL use the link. Mail scanners that fetch links therefore can't spend it.

#### Scenario: Scanner opens the link
- **WHEN** an email security scanner fetches the link before the member clicks it
- **THEN** the link is still unused, and the member can sign in with it

### Requirement: Sign-in email delivery
Sign-in emails SHALL be sent through Resend, from the configured sender, with links built on
`Panopticon:PublicUrl`. Outside the Development environment the server SHALL NOT start without a Resend API
key, a sender and a public URL. In Development with no Resend key, the server SHALL log the link instead.

#### Scenario: Missing key in production
- **WHEN** the server starts outside Development with no `Panopticon:Resend:ApiKey`
- **THEN** start-up fails, naming the missing setting

#### Scenario: Local development
- **WHEN** the server runs in Development with no Resend key and a member asks for a link
- **THEN** the sign-in link appears in the server log, and no email is sent

#### Scenario: Delivery fails
- **WHEN** Resend rejects or can't be reached for a member's sign-in email
- **THEN** the failure is logged, and the page still says to check their email

### Requirement: Limiting sign-in requests
The server SHALL limit sign-in link requests per email address and per client address. A request over the
limit SHALL be refused with a message to try again later, and no email SHALL be sent.

#### Scenario: Repeated requests
- **WHEN** someone asks for links for one address more than 5 times in 15 minutes
- **THEN** further requests are refused for a while, and no further email is sent

### Requirement: Sessions
Confirming a link SHALL start a session, held in an HTTP-only cookie that is `Secure` outside Development. A
session SHALL expire 7 days after its last use. Each use SHALL extend it.

#### Scenario: Active use
- **WHEN** a member uses the dashboard every day
- **THEN** they stay signed in

#### Scenario: Idle for a week
- **WHEN** a member doesn't use the dashboard for more than 7 days
- **THEN** their next visit asks them to sign in again

### Requirement: Signing out
A signed-in member SHALL be able to sign out. Signing out SHALL end that session on the server, so the cookie
no longer works anywhere.

#### Scenario: Sign out
- **WHEN** a member signs out
- **THEN** the dashboard shows the sign-in page, and a copy of the old cookie is refused

### Requirement: Dashboard requires sign-in
The dashboard's data and live stream (`/api/changes`, `/api/events`) and every member, token and
device-approval API SHALL answer 401 without a valid session. The app SHALL then show the sign-in page. The
sign-in endpoints and `/healthz` SHALL stay open.

#### Scenario: Not signed in
- **WHEN** someone without a session opens the dashboard
- **THEN** it shows the sign-in page, and no change data is sent to them

#### Scenario: Session ends while watching
- **WHEN** a member's session ends (sign-out elsewhere, or removal) while their dashboard is open
- **THEN** the dashboard stops receiving updates and shows the sign-in page

### Requirement: Cross-site requests refused
Requests that change state with a session cookie (sign-out, invites, removals, renames, approvals, revocations)
SHALL be refused unless they come from the dashboard itself. Another site therefore can't make a signed-in
member's browser perform them.

#### Scenario: Forged request
- **WHEN** another website makes a signed-in member's browser post a removal to Panopticon
- **THEN** the server refuses it, and nobody is removed
