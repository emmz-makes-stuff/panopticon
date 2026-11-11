# Tasks

## 1. Web: the Tags section redraws on its own

- [ ] 1.1 Split the Project page's Tags section so its rows can be redrawn from a new tag list, without the rest of the page (D2). The list element and the add form are kept, and the heading, empty state, viewer hint and the add form's limit help follow the new count. A row in rename mode is kept when its tag id is still there, with its typed text and focus, and its Cancel shows the new name. A row whose tag has gone goes, and the status line says "`PAN/bug` was deleted." Add that copy to `web/DESIGN.md`'s Project page Tags section. The page's own add, rename and delete still call `load()`. Verify with `projects.test.ts` tests that call the redraw directly: rename, add (its sorted place and count), delete, an open rename kept with its text, an open rename whose tag is deleted, and typed add-form and invite-form text left alone. Then `make web-test` → `WEB_TEST_EXIT:0`.

## 2. Web: the Project page follows the stream

- [ ] 2.1 Open an `/api/events` stream through `env.createEventSource` once the Project page's first load renders (D4), and close it in the view's teardown. A first load that 404s or fails opens no stream. On a `project-tags-changed` event for this Project (ids compared without regard to case), refetch `GET /api/projects/{id}` and redraw the rows with its tags (D1). Each refresh takes a ticket, so only the latest answer applies, and none after teardown (D3). A failed refresh keeps the rows. A 404 shows "There's no such Project" and closes the stream. Verify with `projects.test.ts` tests using a fake EventSource: each scenario under "The Project page's tags are live", another Project's event ignored, a malformed event ignored, an out-of-order pair of answers, the stream closed on navigation, and a 404. Then `make web-test` → `WEB_TEST_EXIT:0`.
- [ ] 2.2 Refetch once on each reconnect (an `onopen` after the first), and rely on the browser's own retry on errors (D4). Verify with `projects.test.ts`: the first open fetches nothing more, a second open refetches once and shows a tag added meanwhile, and an error changes nothing on the page. Then `make web-test` → `WEB_TEST_EXIT:0`.

## 3. Docs and human verification

- [ ] 3.1 Update `docs/DISCOVERY.md`: drop the Tags section's "Not built" note, and say the Project page's Tags list is live (tags only, with counts as of the last list change). Verify by reading it against the spec delta.
- [ ] 3.2 Human verification (stop and ask Emmz): two browsers signed in as different members of one Project, both on its page. In one, add, rename and delete tags, and add a tag from a change's picker. The other's Tags list follows each, an open rename in the second browser survives an edit to another tag, and a delete of the tag being renamed closes it with the status line. Verify with Emmz's PASS, recorded in a devlog note.

## Workflow follow-up

- Whole-change review, then offer `code-review`, then offer `/opsx:archive`.
