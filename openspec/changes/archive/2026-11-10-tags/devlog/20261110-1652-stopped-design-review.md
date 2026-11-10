# Stopped: design review of the tag UI (§7.1)

<!-- Section: 7 · Task: 7.1 · Base: 4548970 · Uncommitted: web/DESIGN.md -->

## What's done

7.1's design is written into `web/DESIGN.md` as a new "Tags" section, before "Accessibility floor". It's formatted
with Prettier (`WEB_FORMAT_EXIT:0`), and `VALIDATE_EXIT:0`. It's uncommitted and 7.1 is unticked, on purpose.

## The design in brief

- **The idea:** the border says who can see a tag. A solid border is a Project's tag, which every member sees. A
  dashed border is your personal tag, which only you see. It reuses the "Personal" plate's and the empty bar's dash.
- **No new colours:** never teal, never `--spark`.
- **The chip:** Atkinson at 12.8px, a 1px `--ink-muted` border and a 3px radius, so it reads apart from code plates
  and agent pills.
  - **Two tones:** the distinguishing part is `--ink` ("bug", "onboarding"), and what comes before it is muted
    ("PAN/", "PAN/area:").
  - **The code:** project chips always carry it (`PAN/bug`).
- **Where chips sit:**
  - **On a card:** under the change name, before the bars, eight at most, then "+N more".
  - **On a history row:** after the meta line.
  - **On the change page:** in the header.
- **The picker:** "Edit tags" opens an inline panel, not a modal. It has a search and real checkboxes with an `--ink`
  ✓, never a lit cell, because a lit cell on that page means a done task. Toggles apply at once, with a status line,
  and "+ Add PAN/…" makes the tag and puts it on the change. Personal tags aren't edited there: a muted line points
  to `panopticon tags`.
- **The Project page's Tags section:** it sits between "Link a repo" and "Members".
  - **The rows:** a chip and its count.
  - **Owners and editors:** Rename turns the row into an input in place. Delete confirms with the count, through
    `env.confirm`. An add form follows.
  - **Viewers:** a muted line instead.
- **The filter:** a "Tags" multi-select button in the bar, after Status. It opens a popover with a search and groups
  ("Yours", then by Project code), with key-only "any value" entries above their values.
  - **The chosen tags:** they show as removable chips after "Showing changes with all of:".
  - **A tag the address names but the choices don't:** it shows as an "Unknown tag" chip.
- **The states:** each part has a table covering viewer, editor, empty and error.

## The question for Emmz

Please review `web/DESIGN.md`'s "Tags" section before §8 builds it. In particular:

1. Do the solid and dashed borders work as the shared-or-only-you cue?
2. Should chips sit under the change name, ahead of the bars? The alternative is after the agents, at the card's
   foot.
3. Should project chips repeat the code (`PAN/bug`) even on a card that already shows the `PAN` plate? The spec's
   "Shown with the code" says yes, and I've followed it.
4. Is eight chips, then "+N more", the right cap on cards?

Recipe:

```sh
sed -n '/^## Tags/,/^## Accessibility floor/p' web/DESIGN.md
```

## On PASS (or with changes)

- Apply any changes to `web/DESIGN.md`, and record her answers in a devlog note.
- Tick 7.1, commit as `feat(tags): design the tag UI (§7)` with the body line `- 7.1 …`, then run the context
  check.
- Go on to §8.1: the tag types in `types.ts`, a shared `tagChips` helper, and chips on cards, history rows and the
  change page.

## Next

- Waiting on Emmz's review of the "Tags" section in `web/DESIGN.md` (the questions above). Nothing else is in flight.
  The working tree holds only the `web/DESIGN.md` change and this note.
