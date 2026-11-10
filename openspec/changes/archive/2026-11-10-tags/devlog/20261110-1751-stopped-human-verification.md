# Stopped: §10.3 needs Emmz's human verification

<!-- Section: 10 · Tasks: 10.1–10.2 done (uncommitted, unticked), 10.3 is this stop · Base: 0c0bb58 -->

§10.1 and §10.2 are built and green, and left uncommitted on purpose. §10 lands as one commit once 10.3 passes.

## Uncommitted, all green

- **10.1:**
  - The README gets a new `### Tags` section (the two kinds, who manages and who sees them, rename and delete, the
    picker, `tag_change`, `panopticon tags`, chips, the Tags filter and the address). The Projects "Roles" bullet now
    says that owners and editors manage tags.
  - `docs/DISCOVERY.md` gets a "Built in the `tags` change" paragraph. It records that the Project page's Tags list
    doesn't update live, which Emmz deferred.
- **10.2:** `ContainerEndToEndTests`.
  - Step 4a runs the published `panopticon tags add dev`.
  - Step 5 checks that the card's personal tags are `[dev]`.
  - Step 5a: the owner adds `bug` to the Project, and puts it on the change through `POST /api/changes/{id}/tags`.
    The card then shows `bug` and `dev`.
  - Step 7 checks that the archived history row keeps both.
  - The test gains a `RunCliAsync(params string[] args)` helper.
- **Gates:** `E2E_EXIT:0`, then `make gates`: `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` `FORMAT_EXIT:0`
  `WEB_INSTALL_EXIT:0` `WEB_BUILD_EXIT:0` `WEB_TEST_EXIT:0` `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0` `VALIDATE_EXIT:0`
  `GATES_EXIT:0`.
- **E2E gotcha:** an xUnit `Assert.Equal` on a tuple of `List`s compares by reference. Assert each list on its own.

## The question

Do the parts below PASS, judged against `web/DESIGN.md`? Emmz gives a PASS or a FAIL, with notes, for each of A to D.

The two departures from `web/DESIGN.md`, which Emmz should accept or reject:

1. **In the picker:** a parse error shows only when the typed text matches no listed tag.
2. **In the Tags filter:** a click outside closes the popover, but leaves focus where the click went. Only Escape
   returns focus to the button.

## Recipe

**Setup.** From the repo root:

```sh
docker compose up -d --wait
npm --prefix web ci && npm --prefix web run build
make install
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

1. In browser 1, sign in at <http://localhost:7480> as `dev@localhost.test`. The link is in the server log.
2. Create a Project `PAN`, and invite a second address (e.g. `sam@localhost.test`) as **editor**.
3. In browser 2, a private window, sign in as `sam@localhost.test`.
4. In this repo, run `panopticon login --server http://localhost:7480` as dev. Link the repo to `PAN` with a
   `.panopticon.json`, run `panopticon tags add dev "language: csharp"`, and have an agent report a change.

**A. Chips, the picker and the Project page** (browser 1)

- **Card:** the chips sit under the change name. `PAN/…` is solid, and `dev` is dashed, with "(only you see this)"
  on hover. The quiet-card dimming applies to the chips.
- **Project page, Tags section:**
  - Add `bug` and `area: onboarding`.
  - Rename one inline, and check that Escape cancels.
  - Delete one: the confirm says how many changes carry it.
- **Change page, Edit tags:**
  - Search, then toggle a tag. Type `zone: x`, then use **+ Add PAN/zone: x**.
  - Type `bad key` to see the inline error (departure 1).

**B. The Tags filter** (dashboard, then History, then Metrics)

- **The button:** it reads "Any tags", and opens to "Yours", then "PAN", with "language … any value" and
  "language: csharp" indented under it.
- **Search:** "Find a tag" narrows the list.
- **Choosing:** choose two tags. The button says "2 tags", the chips line says "Showing changes with all of:", and
  each × removes one.
- **Back/Forward:** step between your choices.
- **Closing:** Escape closes and refocuses the button. A click outside closes (departure 2).
- **On a phone width:** the control takes a whole row.

**C. Live, with two members** (browsers 1 and 2 side by side on the same change and dashboard)

- **Live edits:** in browser 2 (sam, editor), rename `bug` to `defect`, and put a tag on the change. Browser 1 shows
  both at once, with no reload.
- **Personal tags stay private:** browser 2 never shows dev's `dev` or `language: csharp` chips, nor offers them in
  its Tags filter.
- **Deleting under a filter:** delete a tag that browser 1's dashboard is filtered on. The card goes, and "No changes
  match" offers Clear filters.

**D. A real agent** (Claude Code in this repo, with `panopticon mcp` configured)

- Ask it to call `list_projects`, then `tag_change` to add `bug` to the change. The card updates.
- Ask it to add a tag that doesn't exist. It's refused, and the error lists the Project's tags.

## On PASS

1. Write a devlog note recording Emmz's PASS for each part, and her answer on both departures.
2. Tick 10.1 to 10.3, write the section-done note, and make one commit:
   `feat(tags): docs, container e2e and human verification (§10)`.
3. Then "Finishing a change":
   - Self-review `git diff 7a92cd4~7..HEAD`. The first section's base is the commit before §1, found with
     `git log --oneline | grep 'tags'`.
   - Write the change-done note.
   - Offer `code-review`, then `/opsx:archive`.
   - Raise the deferred live Project-page Tags list as a follow-up.

## On FAIL

Add a `## 11. Review fixes: human verification` section to `tasks.md` with one task per failure. Agree it with Emmz,
build it with the section loop, and then ask for the failed parts again.

## Next

Waiting for Emmz's PASS or FAIL on A to D, and her answer on the two departures.
