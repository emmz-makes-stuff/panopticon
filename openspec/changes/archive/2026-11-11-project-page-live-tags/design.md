# Design

## Context

- **The stream:** `GET /api/events` already sends `project-tags-changed` to every member of a Project, in any role, on
  each tag add, rename or delete (`DashboardEndpoints.StreamAsync`, tags D6). Its data is `{ projectId, tags }`, with
  the tags' ids, keys and values, but **no change counts**.
- **The other live views:** the dashboard (`connectLive` in `web/src/live.ts`) and the change page (`follow()` in
  `web/src/views/change.ts`) each open their own `EventSource` through `env.createEventSource`, and close it in the
  view's teardown. The change page refetches on a reconnect (`onopen` after the first), and keeps what it shows if a
  refetch fails.
- **The Project page:** `projectView` in `web/src/views/projects.ts` loads `GET /api/projects/{id}` (with
  `tags[].changeCount`), and `render()` rebuilds the whole page. Its own tag writes call `load()`, so they rebuild it
  all, including an open rename form and typed input. It has no stream, and its teardown does nothing.
- **There's no tags-only endpoint.** The tags come from `GET /api/projects/{id}`.

## Goals / Non-Goals

**Goals:**
- A web-only change. No server, API or contract changes.
- A live update touches only the Tags section's rows. The rest of the page, and its inputs, stay as they are.

**Non-Goals:**
- Live members, roles or name. Live counts on tagging (proposal, out of scope).
- A shared live-stream module for all three views. Two views already open their own streams, and a third follows that
  pattern. Pulling them together is a refactor for another day.

## Decisions

### D1. Refetch the Project, and use only its tags

On a `project-tags-changed` event whose `projectId` matches this page (compared without regard to case, as in
`retagCard`), the page fetches `GET /api/projects/{id}`. It takes `tags` from the answer and redraws the Tags rows with
them.

- **Why refetch:** the event has no counts, and an add from a change's picker is already on one change, so it would
  show the wrong count ("No changes" where it should be "1 change").
- **Alternative, apply the event's tags directly** and keep counts by id: one request fewer, but an added tag's count
  would be wrong. Rejected.
- **Alternative, a new `GET /api/projects/{id}/tags`:** it would cost a server change and a contract, to save a small
  members payload. Not worth it at this size.

### D2. Redraw the rows, not the page

The Tags section keeps its list element and the add-tag form. A live refresh replaces the list's rows, and updates the
add form's limit help (200 tags). The heading, the empty-state text and the viewer hint follow from the new count. The
role (`canManage`) comes from what the page has already: a role change isn't live (Non-Goals).

- **A row in rename mode is kept.** If its tag id is still in the new list, its form, the text typed so far and focus
  stay as they are. Its saved copy of the tag gets the new text, so Cancel shows the current name. Its place in the
  sorted list follows the new order.
- **If the tag being renamed has gone,** the row goes, and the page's status line says "`PAN/bug` was deleted." This
  copy goes in `web/DESIGN.md`.
- **The page's own writes** still call `load()`, as today, and so rebuild the page. The event for that write then
  arrives too, and refreshes the rows once more. That's harmless, and it keeps the page's existing behaviour.

### D3. One ticket for refreshes

Each refresh takes a ticket number. Only the latest one's answer is applied, and none after the page is left, so a
slow answer can't overwrite a newer one. This is the change page's pattern. A failed refresh keeps the rows as they
are, and the next event or reconnect tries again.

- **A 404** means the viewer is no longer a member. The page shows its usual "There's no such Project" state, as a
  reload would, and closes the stream.

### D4. The stream's life

- **Opening:** the stream opens once the first load has rendered the page. A 404 or failed first load opens no
  stream.
- **Reconnecting:** `onopen` after the first one triggers one refresh. On an error, the page relies on the browser's
  own retry, like the change page, and shows no offline state.
- **Closing:** the view's teardown closes the stream.

## Risks / Trade-offs

- **[A burst of tag edits sends one GET each]** → The ticket drops all but the latest answer, and tag edits are rare,
  human-speed actions. No debounce.
- **[Counts go stale after tagging]** → Accepted, and in the spec ("Counts are not live"). A reload fixes them.
- **[A third copy of the stream wiring]** → Kept small, and the shared-module refactor is noted as a Non-Goal, not
  hidden.

## Migration Plan

None. A web-only change that ships with the next image. Rollback is the previous image.
