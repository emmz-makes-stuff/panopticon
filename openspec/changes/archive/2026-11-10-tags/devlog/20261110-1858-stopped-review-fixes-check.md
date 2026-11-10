# Stopped: §11.8 needs Emmz's check of dashboard live tagging

<!-- Section: 11 · Tasks: 11.1–11.7 done (uncommitted, unticked), 11.8 is this stop · Base: 2740da3 -->

§11 was agreed with Emmz in full: all of 11.1–11.8, from the `code-review` (high) findings plus mine in
`20261110-1835-change-done.md`. 11.1–11.7 are built and green, and left uncommitted until 11.8 passes.
`make gates`: `BUILD_EXIT:0` `AOT_EXIT:0` `TEST_EXIT:0` `FORMAT_EXIT:0` `WEB_INSTALL_EXIT:0` `WEB_BUILD_EXIT:0`
`WEB_TEST_EXIT:0` `WEB_FORMAT_EXIT:0` `WEB_LINT_EXIT:0` `VALIDATE_EXIT:0` `GATES_EXIT:0`. Vitest: 396/396.

## What changed

- **11.1, the spec failure:** a tag edit republishes the card with an unchanged `lastReportedAt`, and `upsert` dropped
  it as not newer. This failed live-dashboard's "An editor tags a change". The HV in §10 missed it, because the change
  page refetches on any event, and Project tag renames use `project-tags-changed`.
  - **The fix in `app.ts`:** `upsert` now applies a card at the same `lastReportedAt` when its tags differ
    (`sameTags`, via `replaceQuietly`: no lighting, and it keeps its "updated" mark). It ignores an exact duplicate.
    `render` takes the server's card on a tie with other tags.
  - **Tests:** `live.test.ts` "connectLive, a change tagged" (3 tests).
- **11.2:** `tagFilter.ts` `isKeyOnly` now goes by the choice's kind (`k:`, or a personal `p:<key>` with values), so a
  Project's bare `area` beside `area: onboarding` is its own row and chip.
- **11.3:** `tagPicker(env, changeId, onCard)` reports each POST's card, and `change.ts` stores it in `latest`, so
  choosing another document keeps the chips. The test was checked failing without the fix.
- **11.4:**
  - **One server order:** `ProjectTagOrder.InTagOrder` sorts ordinally, in memory. It's used by `ListAsync`,
    `PublishAsync`, `ListProjectsTool`, `TagChangeTool` and `ChangeCard`.
  - **A stable key-alone spelling:** the history's key-alone choice is spelled as the valued tag whose key sorts last
    ordinally. The web dashboard's `tagChoices` does the same.
  - **Tests:** `Tags_are_in_ordinal_order_in_the_list_and_the_event_as_on_the_cards` and
    `A_keys_choice_alone_is_spelled_as_the_valued_tag_whose_key_sorts_last`. I didn't prove the ordinal test fails
    under the old SQL ordering, because that depends on the test database's collation.
- **11.5:** `TagsCommand.Remove` tolerates a null entry (`t?.Trim()`). There's a new test.
- **11.6:**
  - **`TagChangeTool`:** `Parse` uses `TagText.TryParseAll`, capped at 200. Its pre-check uses
    `ProjectEndpoints.RoleAsync`, the same lookup `ChangeTagging.ApplyAsync` repeats in its transaction. Both checks
    are kept on purpose: the first gives a precise message, and the second catches races.
  - **`ChangeEvent.Updated`:** it is now `Updated(LiveCard Live, ChangeScope Scope)`, with `Card` and `CardFor`
    delegating to it.
  - **Web:** the picker sorts with `byTagOrder`, and `filters.ts` exports one `ordinal`.
- **11.7:** tests for a renamed tag's filter chip, a deleted tag leaving the choices (`tagFilter.test.ts`), and an
  editor's Project page (`projects.test.ts`).

## The question

Does a change being tagged now show on another member's open **dashboard** at once, with no reload?

## Recipe

1. Stop the running server. Then rebuild the web and start the server again:
   ```sh
   npm --prefix web run build
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
   ```
2. **Browser 2**, your personal account and PAN's owner: open the **dashboard** and keep it open.
3. **Tag the change:** in browser 1 (dev), open the `tags` change page, choose **Edit tags**, and toggle
   `team: onboarding`. Or ask me to call `tag_change`.
4. **Expect:** browser 2's `tags` card gains or loses `PAN/team: onboarding` at once. It doesn't move or light up.
5. **Optional:** in browser 2, filter the dashboard by `PAN/team: onboarding`, then take the tag off the change in
   browser 1. The card should leave browser 2's dashboard, with "No changes match".

## On PASS

1. Record the PASS.
2. Tick 11.1 to 11.8, write the section-done note, and make one commit,
   `fix(tags): review fixes, live tagging, filter choices and order (§11)`. Include the uncommitted change-done
   note.
3. Offer `/opsx:archive`, and raise the deferred live Project-page Tags list.
4. Remind Emmz that the CLI is still logged in as `dev@localhost.test`, with the personal tags `dev` and
   `language: csharp`.

## On FAIL

Note what showed, and debug 11.1 with the real event. In the browser's network tab, the `change-updated` data should
carry the same `lastReportedAt` with the new `projectTags`.

## Next

Waiting for Emmz's PASS or FAIL on step 4 (and step 5, if she runs it).
