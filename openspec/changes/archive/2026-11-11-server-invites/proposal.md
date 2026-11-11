# Proposal

## Why

The only way onto a server today is a Project invite (or `Panopticon:InitialMembers`). That's fine for a team, but as
the operator of `panopticon.team`, Emmz wants to let friends onto the server to track their own work. Doing that
through a throwaway Project makes no sense. And the Members page only lists people who share a Project, so a friend
who joins without one can't be seen or removed there.

## What Changes

- **Invite to the server.** The Members page gets an Invite form (an email address) for initial members, the
  addresses in `Panopticon:InitialMembers`. Inviting makes the address an active member, or makes a removed member
  active again, and adds them to no Project. Nobody else can invite to the server. Project invites are unchanged.
- **An invitation email.** An invited address gets an email through the existing Resend sender: who invited them,
  and a link to the server's sign-in page. It isn't a sign-in link, so it can't expire or be spent. In Development
  with no Resend key, it's logged like sign-in links.
- **Invites are limited.** Inviting an address that's already an active member is refused, and no email is sent.
  Each inviter has a rate limit, so a stolen session can't use the server to send spam.
- **Initial members see everyone.** For initial members, the member list shows every active member of the server.
  For everyone else, it's unchanged: members who share a Project with them, and themselves.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `team-members`: adds inviting to the server and its email. The member list rule changes for initial members.

## Impact

- **Server:**
  - A new `POST /api/members` endpoint (the `Member` policy and the existing same-origin check).
  - The member-making step of Project invites, shared between both invites.
  - A new email job, an inviter rate limit in `AuthLimits`, and `canInviteMembers` on the session member.
  - `MemberEndpoints.ListAsync` lists every active member for initial members.
- **Web:** an Invite form on the Members page, shown when `canInviteMembers` is set. The page comment that says
  there's no invite is corrected.
- **No migration and no new dependencies.**
