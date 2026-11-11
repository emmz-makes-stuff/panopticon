# Stopped: Emmz to check the Invite form in a real browser

<!-- Section: 2 · Tasks: 2.1 done, 2.2 pending · Base: 4d21fe3 -->

Task 2.1 is built and left **uncommitted**, as §2 isn't finished. Changed files: `web/src/views/members.ts`,
`members.test.ts`, `api.ts`, `testing.ts` and `styles.css`.

- For initial members (`canInviteMembers`), the Members page shows a "Invite someone to the server" section above the
  list, and an intro line reading "Everyone on this server.". Everyone else keeps the intro line "Everyone who shares
  a Project with you." and the hint pointing to Projects.
- On a 201, the address field is cleared, a status line reads "Invited <email>. They'll get an email, and can sign in
  with that address now.", and the list reloads.
- On a refusal, the server's detail is shown inline in the form and the address stays in the field.
- A blank address is caught before anything is sent.
- Styling reuses `.invite-form`; the only new rules are `.members-invite` margins and its muted paragraph.

Gates so far for §2: `WEB_BUILD_EXIT:0`, `WEB_TEST_EXIT:0` (463 passed), `WEB_LINT_EXIT:0`. `WEB_FORMAT_EXIT:1`
first time; after `prettier --write` on `members.test.ts`, `WEB_FORMAT_EXIT:0`. The full `make gates` hasn't run for
§2 yet.

**Waiting on:** Emmz to run the recipe in task 2.2 and confirm. The question: does the invite flow look and behave
right in a real browser?

## Next

- When Emmz confirms: write a note with her answer, tick 2.2, run `make gates`, write the section-done note, and
  commit §2 as `feat(server-invites): … (§2)`. Then the whole-change review (`git diff 2c569ee..HEAD`), offer
  `/code-review`, then offer `/opsx:archive`.
- If she wants changes, add them to 2.1's work (still uncommitted) and ask her to check again.
- `openspec/changes/agent-interaction/` is a separate, untracked change. Never `git add -A`.
