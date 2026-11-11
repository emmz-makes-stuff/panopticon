# Design

## Context

- **Project invites** (`ProjectEndpoints.InviteAsync`) make an address a member through
  `MemberInvites.FindOrAddAsync`. It adds a new member, or makes a removed one active again. A unique violation
  means someone invited the same new address at the same moment, which returns a 409. No email is sent.
- **Initial members** are read from `Panopticon:InitialMembers` on every call (`InitialMemberList`).
  `MemberEndpoints.CanRemoveMembersAsync` checks them, and the session member carries the result as
  `canRemoveMembers`, which the Members page uses to show Remove.
- **Removal** locks the member rows `FOR NO KEY UPDATE`. Invites and Project creation lock the caller's row first
  (review fix 12.5), so a removed member can't act halfway through their removal.
- **Email:** sign-in links go through `EmailOutbox`, which runs `IEmailJob`s on a background service. They are sent
  by `ResendEmailSender`, or by `LoggingEmailSender` in Development with no key. Links are built on
  `Panopticon:PublicUrl`.
- **Rate limits:** `AuthLimits` holds sliding-window `PartitionedRateLimiter`s, for sign-in per email and per client,
  device starts, and approvals.

## Goals / Non-Goals

**Goals:**
- One way to make a member, shared by both kinds of invite, with the same race handling.
- The email carries no secret, so forwarding it or having it scanned does no harm.

**Non-Goals:**
- Emails for Project invites. They stay silent.
- Pending invites, accept or decline steps, and invite expiry. An invite makes the member at once, as Project invites
  do.
- Letting members other than initial members invite to the server, or making the initial-member list editable from
  the dashboard.
- Showing who invited a member.

## Decisions

### D1. `POST /api/members` shares Project invites' member-making step

The endpoint, in order:

1. Check the caller is an initial member (403). Then validate the address (400), and check the rate limit (D3, 429).
2. In one transaction, lock the caller's row and the address's row `FOR NO KEY UPDATE`, in id order as a removal locks
   them, so the two can't deadlock. Refuse with 403 if the caller has been removed.
3. If that read finds an active member with the address's key, return 409 "already a member".
4. Otherwise call `MemberInvites.AddUnlessActiveAsync`, the same locking read and add as `FindOrAddAsync`, which
   reports whether the address was already active. If it was, return the same 409.
   - Step 3's statement can't see a member that a concurrent invite committed while this one waited on the
     caller's lock: its snapshot is older. Step 4's new statement can. Found in §1, where two concurrent invites both
     got a 201.
   - A unique violation, from a concurrent uncommitted insert of the same address, also returns 409.
5. Commit, then queue the email (D2), and return 201 with the `MemberView`.

The email is queued only after the commit, so a refused or rolled-back invite never sends one.

**Alternative:** a separate server-invite table with pending invites. Rejected: the product already treats an invite
as membership (Project invites), and a pending state would need expiry and accept flows for no gain.

### D2. `ServerInviteEmail` job

`ServerInviteEmail` is an `IEmailJob` beside the sign-in link job. Its subject is "<inviter> invited you to
Panopticon". The text and HTML bodies say who invited them, link to `<PublicUrl>/` (the sign-in page), and say to sign
in with this address. The HTML body escapes the display name. Unit tests check both bodies, with a name full of
markup, and that the body contains no `/sign-in/` token path. It's queued with `EmailOutbox.Enqueue`. A full outbox
logs and drops the email as sign-in does, and the member stays added: they can still sign in from the page.

### D3. A per-inviter limit in `AuthLimits`

`TryServerInvite(memberId)` allows 20 invites per member in a sliding hour. It's checked before the transaction, so a
refused request does no database work. The only callers are initial members, so the limit guards against a stolen
session, not ordinary use.

### D4. Who sees what

- `SessionMember` gains `CanInviteMembers`. It has the same value as `CanRemoveMembers` today, but a separate name,
  so the UI says what it means and the two can diverge later.
- `MemberEndpoints.ListAsync` takes `InitialMemberList`. For an initial member it drops the shared-Project condition
  and lists every active member. For everyone else the query is unchanged.
- **Web:** `views/members.ts` adds an Invite form (an email field and an Invite button) above the list when
  `canInviteMembers` is set. It shows errors inline in text, adds the new member to the list on 201, and the page
  comment is corrected. Existing styles are used, so this needs no `frontend-design` pass.

## Risks / Trade-offs

- [Someone invites a typo'd address, and a stranger gets a member account] → the address must be typed exactly. The
  invite shows in the list at once, and Remove undoes it. The email holds no secret, so the stranger can sign in only
  by asking for a link to that address, which they would then own anyway.
- [An initial member removed from configuration keeps inviting] → `InitialMemberList` is read on each call, so a
  config change takes effect at once.
- [Email delivery fails] → the member is still added. The inviter can tell them to sign in from the page, as with
  Project invites today.
