# Tasks

## 1. Tag syntax and the data model

- [x] 1.1 Add `Tag` and `TagText.TryParse` to `Panopticon.Contracts` (D1): key and value rules, the first `:` splits, trimming, the canonical text, case-insensitive equality and the error messages. Verify the "Tag syntax" scenarios with unit tests, including `link: http://x`, `key:`, `: value`, a 41-character key and a value with a line break.
- [x] 1.2 Add `ProjectTag`, `ChangeProjectTag` and `ChangeAgentTag` with their keys, cascades and indexes, in one migration with a down migration (D2). Verify with migration tests: the migration applies and rolls back, two tags equal without regard to case can't coexist in one Project but can in two, and deleting a tag or a change removes its `change_project_tags` rows.

## 2. Server: a Project's tags

- [x] 2.1 Add the Project tag endpoints (add, rename, delete) and `tags` with `changeCount` on `GET /api/projects/{id}` (D3): the owner/editor check, D1 validation (400), duplicates and the 200 limit (409, under the Project row lock), and 404 for non-members. Verify "A Project's tags", "Managing a Project's tags", "Renaming a tag", "Deleting a tag" (server side) and the `projects` "Membership roles" and "Project page" scenarios with endpoint tests, including two concurrent adds at 199 tags leaving exactly 200.
- [x] 2.2 Publish `ProjectTagsChanged(projectId, tags)` after each tag write, and send `project-tags-changed` to subscribers in that Project's scope (D6). Verify with `StreamAsync` tests: a `PAN` member gets the event for a rename, and an `ACME`-only member doesn't.

## 3. Server: tagging changes

- [x] 3.1 Add `POST /api/changes/{id}/tags` (D4): the owner/editor check, tags from the change's own Project only, no project tags on personal changes, archived changes allowed, idempotent add/remove, the caller's card returned, and the card republished. Verify "Tagging a change on the dashboard" with endpoint tests: an editor, a viewer (403), an outsider (404), another Project's tag id (400), and an archived change.
- [x] 3.2 Add `tags` to `list_projects`, and the server's `tag_change` MCP tool with its contracts (D4): text tags, finding the change (active first, then the latest archived), all checks done before any write, and errors naming the problem (listing the tags for an unknown one). Verify "Tagging a change over MCP", "Agents only use existing tags" and "An agent sees the tags" with MCP endpoint tests, including a viewer's agent, an unknown tag (nothing changed, the tags listed) and a bootstrap reporter.

## 4. Server: personal tags in reports

- [x] 4.1 Add `PersonalTags` to `ReportProgressArguments` and `ReportInput`. Validate them before recording (20 at most, malformed refused), and replace the agent's `change_agent_tags` set inside the report's transaction, leaving it alone when the field is absent (D5). Verify "Personal tags in reports" and "Personal tags on a change" with recorder and MCP endpoint tests: two agents of one member, a tag removed, `null` versus `[]`, an archived change frozen, and a linked personal change keeping its agents' tags when it moves into a Project.

## 5. Server: tags in every read, per viewer

- [x] 5.1 Add `projectTags` and `personalTags` to `ChangeCard` (so to `ChangePage` and history entries too), filling the personal ones from the viewer's own agents in `ChangeCards`, `ChangePages` and the history (D6). Verify "Tags shown on changes" and "Personal tags are private" with endpoint tests, and extend the outsider sweep test so Sam's responses never contain Emmz's personal tag text.
- [x] 5.2 Make the live stream per viewer (D6): `Updated` carries the server-side map of personal tags by member, and `StreamAsync` writes each subscriber its own. Verify "Tag edits are live" (server side) with `StreamAsync` tests: Emmz's stream carries her tag on a shared `PAN` card, and Sam's stream for the same event never contains it.
- [x] 5.3 Add the Tags filter to `HistoryFilter` and the history and metrics endpoints: repeated `tag` values in the `t:`, `k:` and `p:` forms, one `EXISTS` each, a 400 for a malformed one, and at most 20 (D7). Add `tags` to `HistoryChoices`, with personal tags from the viewer's own agents only. Verify "Filter by tags" and "Tag filter choices" (history), and the `change-metrics` "Metrics by tag" and "Metrics by a personal tag" scenarios, with endpoint tests.

## 6. CLI: personal tags and `tag_change`

- [x] 6.1 Replace `SavedLogin` with `CliConfig` (server, token, tags), with read-modify-write for every writer (D8). Verify with `CliConfigFile` tests: `login` keeps the tags, a tag write keeps the login, and the 0600 file and 0700 directory hold.
- [x] 6.2 Add `panopticon tags`, `tags add` and `tags remove` (D8), and list them in the help. Verify "Personal tags in the config" with CLI tests: list, add, case-insensitive remove, malformed, duplicate, the 21st tag, and removing a missing one, each leaving the file unchanged on error.
- [x] 6.3 Make the bridge send `PersonalTags` with every report, read fresh each time, and report a malformed hand-edited tag as a tool error naming the file, not the token (D8). Verify "Personal tags sent with reports" with bridge tests, and an end-to-end CLI test against the server where adding a tag shows up on the next report without a restart.
- [x] 6.4 Add the bridge's `tag_change`, filling `project` from `.panopticon.json` and `repo` from git when they're left out, and passing errors through (D8). Verify "Tagging through the bridge" with bridge tests: a linked repo, no link file, and a server refusal listing the tags. Document `panopticon tags` and `tag_change` in `docs/cli.md`.

## 7. Design pass

- [x] 7.1 Design the tag chips, the change-page picker (with "+ Add"), the Project page's Tags section (inline rename, delete confirm with the count) and the multi-select Tags filter with the `frontend-design` skill, within the existing `web/DESIGN.md` system. Record them in `web/DESIGN.md` (D9). Verify that `web/DESIGN.md` has a section for each, with ASCII layouts and states (empty, viewer, editor, error).

## 8. Web: tags in the views

- [x] 8.1 Add the tag types to `types.ts` and a shared chips helper, and show the chips on cards, history rows and the change page, with no row when there are no tags (D9). Verify "Tags shown on changes" with Vitest tests on each view.
- [x] 8.2 Add the Project page's Tags section: the list with counts for everyone, plus add, inline rename and delete with a confirm for owners and editors, and inline errors from the API (D9). Verify the Project-page parts of "Managing a Project's tags", "Renaming a tag" and "Deleting a tag" with Vitest tests, including that a viewer sees no controls.
- [x] 8.3 Add the change page's picker for owners and editors: search, toggling tags, "+ Add `PAN/…`" (two calls), and inline parse errors (D4, D9). Verify "Tagging a change on the dashboard" and "Adding a tag from the picker" with Vitest tests, including a viewer seeing chips with no picker.
- [x] 8.4 Handle tags live: `change-updated` cards with their tags, and `project-tags-changed` renaming and dropping by id on the dashboard and the change page (D6). Verify "Tag edits are live" (web side) with `live` and view tests, including a filtered dashboard dropping a card whose tag was removed.

## 9. Web: the Tags filter

- [x] 9.1 Add `tags` to `filters.ts`: the `t:`, `k:` and `p:` forms, canonical ordering in the address, AND matching, key-only matching, and dropping malformed values or any beyond 20 (D7). Verify "Filter by tags" and the modified "Filters combine" with `filters` unit tests.
- [x] 9.2 Add the multi-select Tags control to the filter bar on the dashboard, history and metrics, with the grouped choices (personal first, then by Project code, key-only entries above their values), removable chips, and clear (D7, D9). Verify "Tag filter choices" and "Filters in the address" for tags with Vitest tests on each page, including Back/Forward between tag choices.

## 10. Docs, integration and human verification

- [x] 10.1 Update the README (tags, who sees which, `panopticon tags`) and `docs/DISCOVERY.md` (Tags built). Verify by reading both against the specs.
- [x] 10.2 Extend the `make e2e` container flow: an editor adds a Project tag and tags a change, a report carries a personal tag, and the card shows both. Verify with `make e2e` → `E2E_EXIT:0`.
- [x] 10.3 Human verification (stop and ask Emmz): the chips, picker, Project page Tags section and Tags filter as built against `web/DESIGN.md`. Live tag edits between two browsers signed in as different members, with personal tags not crossing. And a real Claude Code calling `list_projects` and `tag_change` through `panopticon mcp`. Verify with Emmz's PASS for each, recorded in a devlog note.

## 11. Review fixes: live tagging, filter choices and order

- [x] 11.1 The dashboard applies a card whose `lastReportedAt` is unchanged but whose tags differ (a tag edit's `change-updated`) in place, with no lighting or "updated" mark; `render` prefers the server's card on a tie (D6). Verify "An editor tags a change" (live-dashboard) with `live` tests, including a filtered dashboard dropping the card when its tag is removed.
- [x] 11.2 The Tags filter treats a choice as "key alone" by its kind (a `k:` choice, or a personal key with values), so a Project's bare tag isn't shown or chipped as "any value" (D7). Verify with a `tagFilter` test of a Project with `area` and `area: onboarding`.
- [x] 11.3 The change page keeps the picker's newer card, so choosing another document doesn't revert the chips (D9). Verify with a Vitest test on an archived change.
- [x] 11.4 The server orders a Project's tags ordinally in memory everywhere (the tags list, `project-tags-changed`, `list_projects`, `tag_change`'s message), and the history's key-alone choice has a stable spelling (D6, D7). Verify with server tests using `a_b` and `ab`, and `Area`/`area`.
- [x] 11.5 `panopticon tags remove` tolerates null entries in a hand-edited config. Verify with a `TagsCommand` test.
- [x] 11.6 Clean-ups: `TagChangeTool` uses `TagText.TryParseAll` (with the cap) and one role check; `ChangeEvent.Updated` holds a `LiveCard`; the picker sorts a new tag as the server does; one ordinal comparer in `filters.ts`. Verify with the existing tests green.
- [x] 11.7 Tests for the self-review gaps: a renamed tag's filter chip shows the new name; a deleted tag leaves the filter's choices; an editor's Project page has no invite or member controls.
- [x] 11.8 Human verification (stop and ask Emmz): with two browsers, tagging a change shows on the other member's dashboard live. Verify with Emmz's PASS, recorded in a devlog note.

## Workflow follow-up

- Self-review the whole change, offer `code-review`, and then `/opsx:archive` (CLAUDE.md, "Finishing a change").
