# Proposal

## Why

When an owner or editor adds, renames or deletes one of a Project's tags, the cards and change pages show it at once.
The Project page's own Tags list doesn't: it is the one place that manages tags, yet it stays out of date until a
reload. `web/DESIGN.md` already says the list "follows `project-tags-changed`". The `tags` change deferred that
(Emmz, 2026-11-10), and this change builds it.

## What Changes

- **The Project page follows the live stream.** While it's open, it listens for `project-tags-changed` events for its
  own Project. Each one refetches the Project and redraws just the Tags list, change counts included. A tag added,
  renamed or deleted by someone else then shows without a reload.
- **Nothing else on the page moves.** The members, roles, name, invite form and add-tag input stay as they are. A tag
  someone is renaming keeps its open form and the text typed so far. If that tag is deleted meanwhile, its row goes
  and the status line says so.
- **Reconnects catch up.** After the stream reconnects, the page refetches its tags once, since events may have been
  missed. It closes the stream when you leave the page.
- **Out of scope (Emmz, 2026-11-11).**
  - **Counts:** they update only when the list changes or the stream reconnects, not each time a change is tagged or
    untagged.
  - **The rest of the page:** members, roles and the Project's name don't update live.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tags`: a new requirement, "The Project page's tags are live", covering what updates, what stays put while it does,
  and reconnects.

## Impact

- **Web:**
  - `web/src/views/projects.ts` (the Project view): it opens its own `/api/events` stream through
    `env.createEventSource`, and the Tags section can be redrawn on its own;
  - `web/src/views/projects.test.ts`;
  - `web/DESIGN.md`: one line of copy, for a tag deleted while it was being renamed.
- **Server:** none. `project-tags-changed` already goes to every member of the Project (tags D6), viewers included.
- **Docs:** `docs/DISCOVERY.md` loses its "Not built" note.
- **No** API, data model, CLI or MCP change.
