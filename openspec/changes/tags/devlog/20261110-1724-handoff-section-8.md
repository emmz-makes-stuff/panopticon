# Handoff: §8 part-built, 8.1–8.3 done and uncommitted, 8.4 next

<!-- Section: 8 · Tasks: 8.1–8.3 done (uncommitted, unticked), 8.4 not started · Base: 7a92cd4 -->

The context passed 40% after 8.3, so this hands off mid-section. Nothing is committed for §8 yet: the section lands
as one commit after 8.4.

## Uncommitted, all green

On this tree: Vitest 329/329, `WEB_BUILD_EXIT:0`, `WEB_LINT_EXIT:0`, `WEB_FORMAT_EXIT:0`. The .NET gates weren't
rerun, and §8 touches only `web/`.

- **8.1, chips:**
  - **`tagChips.ts`:** `tagText`, `tagChip`, `tagChips(item, max?)` and `CARD_TAG_LIMIT = 8`.
  - **`types.ts`:** `ProjectTagRef` and `PersonalTagRef`, and `projectTags` and `personalTags` on `ChangeCard` and
    `HistoryEntry`.
  - **Where they show:** in `card.ts`, under `.card-change`; in `views/history.ts`, after `.history-meta`; and in
    the change page header.
  - **CSS:** `.tag-chips` and `.tag-chip` (`.personal` is dashed), with the quiet-card rules.
  - **Fixtures:** `testing.ts` `card()` and the `history.test.ts` `entry()` now have empty tag arrays.
- **8.2, the Project page Tags section:** in `views/projects.ts`.
  - **Types:** `ProjectTag` (`id, key, value, text, changeCount`), `ProjectDetails.tags` and `PROJECT_TAG_MAX`.
  - **The section:** `tagSection`, `tagRow` (inline rename, with Escape to cancel; delete through `env.confirm` with
    the count) and `addTagForm`.
  - **Tests:** a new `describe("/projects/{id} tags")` in `projects.test.ts`, with a `clickTagButton` helper. ESLint
    forbids `!` non-null assertions, so the helper throws instead.
- **8.3, the picker:**
  - **`tagSyntax.ts`:** `parseTag` and `sameTag`, mirroring the server's `TagText` messages.
  - **`tagPicker.ts`:** `tagPicker(env, changeId)` returns `{ element, update(card) }`. The element is persistent, and
    `views/change.ts` creates it once and calls `tags.update(card)` in `renderHead`, so a live refresh doesn't close
    an open panel.
  - **Who gets the button:** the picker learns the role from `GET /api/projects/{id}` and shows "Edit tags" only to
    owners and editors. It refetches on open, and re-renders the list only when the tags changed, so focus isn't
    lost.
  - **Adding:** "+ Add PAN/…" sends `POST /api/projects/{id}/tags`, then `POST /api/changes/{id}/tags {add:[id]}`.
  - **CSS:** `.tag-picker*`, with an ink ✓ in a rule square (`appearance: none`).
  - **Tests:** `tagPicker.test.ts` (8 tests).

## Gotchas

- **Detached checkboxes don't toggle.** The `startTestShell` root is detached, and a detached checkbox fires no
  `change` on `.click()` in jsdom. Focus also doesn't move. `tagPicker.test.ts` appends `shell.events` to
  `document.body` and removes it in `afterEach`.
- **Prettier rewraps code after edits**, so re-read a file before an exact-string edit.

## A small departure from `web/DESIGN.md`

In the picker, a parse error shows only when the typed text matches no listed tag, so partial typing doesn't flash
errors. Mention it to Emmz at the §10.3 human check.

## Gaps for later

- **Project page live updates:** `web/DESIGN.md` says the Project page's Tags list follows `project-tags-changed`.
  No task covers this. 8.4 covers only the dashboard and change page, and the Project page has no live stream today.
  Raise it with Emmz: add it to 8.4, or defer it.
- **`tagSyntax.ts` has no tests of its own:** the picker tests cover it. Add a small `tagSyntax.test.ts` in 8.4,
  or in 9.1, which needs tag parsing for the `p:` forms.
- **4 commits are unpushed** on `change/tags`.

## Next

- **§8.4: handle tags live.**
  - `change-updated` cards already carry their tags, because the server fills them per viewer.
  - Handle `project-tags-changed {projectId, tags:[{id,key,value}]}` on the dashboard and the change page: rewrite
    the `projectTags` of cards in that Project by id, renaming, and dropping ids that are gone.
  - On the change page, also refresh the picker's Project tags.
  - Verify with `live` and view tests, including a filtered dashboard dropping a card whose tag was removed. That
    filter is §9's, so for now check that the card's chips update.
  - Look at `live.ts` for the event constants (`PROJECTS_CHANGED` exists; add `PROJECT_TAGS_CHANGED`), and at
    `app.ts` or `views/cards.ts` for the dashboard's event handling.
- **Then the section's end:** self-review, `make gates`, tick 8.1–8.4, the section-done note, and one commit,
  `feat(tags): tags in the views (§8)`.
