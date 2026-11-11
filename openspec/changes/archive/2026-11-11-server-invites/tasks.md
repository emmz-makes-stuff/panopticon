# Tasks

## 1. Server: inviting to the server

- [x] 1.1 Add `ServerInviteEmail` (D2) and `AuthLimits.TryServerInvite` (D3). Verify with unit tests: the subject and both bodies name the inviter and link to `<PublicUrl>/`, a display name of `<b>Emmz</b>` is escaped in the HTML body, neither body contains a sign-in token, and the 21st invite in an hour is refused.
- [x] 1.2 Add `POST /api/members` (D1) with the `Member` policy and the same-origin check, using `MemberInvites`' locking read (`AddUnlessActiveAsync`, D1). It locks the caller first, returns 409 for an active member, queues the email only after the commit, and returns 201 with the member. Verify the "Inviting to the server", "Inviting an existing member", "Invitation email" and "Invite rate limit" scenarios with endpoint tests: a non-initial member (403, nothing sent), an invalid address (400), a removed member made active again with no Projects and no tokens, `Kit@Example.com` against `kit@example.com` (409, no email), the logging sender in Development, and two concurrent invites of the same new address giving one 201 and one 409.
- [x] 1.3 Add `CanInviteMembers` to `SessionMember`, and make `MemberEndpoints.ListAsync` list every active member for initial members (D4). Verify the modified "Member list" scenarios with endpoint tests: an initial member sees an invitee with no Projects, a non-initial member's list is unchanged, and two invitees don't see each other.

## 2. Web: the Invite form

- [x] 2.1 Add the Invite form to `views/members.ts` when `canInviteMembers` is set: an email field and an Invite button, errors (400, 403, 409, 429) shown inline in text, and the new member added to the list on 201. Correct the page comment. Verify with vitest tests: there's no form without `canInviteMembers`, an invite adds a row with a Remove button, a 409 shows "already a member", and the field keeps its value after an error.
- [x] 2.2 Stop for Emmz to check it in a real browser against a local server in Development (the logged email): invite an address, see it listed, sign in as it from the logged sign-in link, and remove it again. Verify: her confirmation, recorded in a devlog note.

## Workflow follow-up

- Whole-change review, offer `/code-review`, then `/opsx:archive`.
