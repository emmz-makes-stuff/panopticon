# Design

## Context

- **Projects are built** (`projects` change, archived 2026-11-10).
  - **Data:** `Project` has a fixed upper-case `Code`, and `ProjectMember` has a role (`ProjectRoles.Owner`, `Editor`,
    `Viewer`). `editor` behaves as a viewer today. Every read path applies `Viewer`/`VisibleTo` (projects D2).
  - **Membership writes** lock the Project row (§12), so the last-owner rule holds under concurrent edits.
- **Agents on a change:** `ChangeAgent` is one row per agent (client@machine, owned by a `Member`) per change, with
  its `LastReportedAt`. Linking moves personal work into a Project together with its `ChangeAgent` rows (projects D4).
- **The live stream** (projects D3): `ChangeBroadcaster` fans `ChangeEvent`s (`Updated(card, scope)`, `Archived`,
  `MembershipChanged`, `MemberRemoved`) into 64-deep queues, one per subscription. `StreamAsync` drops events outside
  its viewer's scope. **Every subscriber of a Project gets the same `ChangeCard` object.**
- **Filters:**
  - **The dashboard** filters its cards in the browser (`web/src/filters.ts`, one value per key, in `FILTER_KEYS`
    order).
  - **The history and the metrics** filter on the server through `HistoryFilter`, one value per key. `HistoryChoices`
    lists the values found among archived changes.
- **The CLI config** (`CliConfigFile`) holds `SavedLogin(ServerUrl, Token)`. It's written atomically, 0600, and
  `Write` replaces the whole file.
- **Contracts** shared by the CLI and the server live in `Panopticon.Contracts`, and the CLI is Native AOT.

## Goals / Non-Goals

**Goals:**
- **One tag syntax**, parsed by the same code in the CLI and the server, so they can never disagree about what's
  valid.
- **Project tags by id.** Renames are one row, deletes cascade, and filter links survive renames.
- **Personal tags that never leak** through the API, the choices or the live stream, with no per-subscriber database
  read.
- **The Tags filter reuses the existing filter paths:** `filters.ts` on the dashboard, `HistoryFilter` on the history
  and metrics.

**Non-Goals:**
- Editing personal tags on the dashboard, tag colours, tags defined server-wide, and agents creating Project tags.
- Live updates on the history and metrics pages. As with membership (projects D3), they follow on their next load.
- Tag descriptions, ordering by hand, and merging two tags.

## Decisions

### D1. Tag syntax in `Panopticon.Contracts`

- **One parser for both sides:** `TagText.TryParse(string, out Tag tag, out string error)`, with
  `Tag(string Key, string? Value)` and its lower-cased `KeyKey`/`ValueKey`.
  - **The rules:** a key matches `^[A-Za-z0-9._-]{1,40}$`. The first `:` splits key from value. The value is trimmed,
    1 to 40 characters, with no `\r` or `\n`. An empty value after a `:` is an error ("`key:` has no value"), not a
    bare key.
  - **The canonical text:** `key` or `key: value`, with one space after the colon. Equality is on
    `(KeyKey, ValueKey)`.
- **Errors** name the tag and the rule broken, e.g. "`has space`: a key can only have letters, digits, `-`, `_` and
  `.`". The CLI and the server use the same messages.
- **Why the Contracts project:** both sides already reference it. The regex uses `[GeneratedRegex]`, which is
  AOT-safe.

### D2. Data model

- **`project_tags`:** `id uuid`, `project_id` (cascade on delete), `key`, `value` (nullable), `key_key`,
  `value_key` (`''` when there's no value, so the unique index needs no `NULLS NOT DISTINCT`), and `created_at`.
  - **Unique:** `(project_id, key_key, value_key)`.
  - **Display:** keeps the casing it was given, and a rename can change the casing.
- **`change_project_tags`:** `(change_id, tag_id)` as the primary key, with both foreign keys cascading. Deleting a tag
  (or a change) cleans up after itself, and "delete from every change, archived included" is one statement.
  - **Same Project only:** the code checks that a tag's Project is the change's Project. A personal change can't hold
    project tags. A change never moves between Projects (moving out of personal only happens before it has any).
- **`change_agent_tags`:** `(change_id, agent_id, key_key, value_key)` as the primary key, plus `key` and `value`. It
  holds the agent's latest personal tag set on that change. It references `change_agents (change_id, agent_id)` with a
  cascade, so when linking moves a `ChangeAgent`, its tags move with it.
  - **Why a table, not `jsonb` on `change_agents`:** the history and metrics filter on it in SQL (D7). An indexed
    `EXISTS` is simpler and safer under EF than querying inside JSON.
- **One migration** adds the three tables. There's no backfill: existing changes simply have no tags.

### D3. Project tag endpoints

- **`GET /api/projects/{id}`** gains `tags: [{ id, key, value, text, changeCount }]`, sorted by key then value
  without regard to case. Any member sees it. `changeCount` (active and archived changes) feeds the delete confirm.
- **`POST /api/projects/{id}/tags`** `{ tag: "area: billing" }` answers 201 with the tag.
- **`PATCH /api/projects/{id}/tags/{tagId}`** `{ tag }` answers 200.
- **`DELETE /api/projects/{id}/tags/{tagId}`** answers 204.
- **Errors:**
  - **400:** a malformed tag, with D1's message.
  - **403:** a viewer.
  - **404:** a non-member, or an unknown tag.
  - **409:** an equal tag already exists (naming it), or the Project already has 200.
- **Locking:** each write locks the Project row (`SELECT … FOR UPDATE`, as membership writes do), so the 200 limit
  holds under concurrent adds. The unique index catches a duplicate either way, and its violation maps to 409.
- **Live:** after commit, each write publishes `ProjectTagsChanged(projectId)` (D6).

### D4. Tagging a change

- **On the dashboard:** `POST /api/changes/{id}/tags` `{ add: [tagId], remove: [tagId] }` answers 200 with the
  change's card as the caller sees it.
  - **Who:** owners and editors of the change's Project.
  - **Errors:** 403 for a viewer, 404 for a change that isn't visible, and 400 for a tag id that isn't the Project's,
    a personal change, or an empty request.
  - **Idempotent:** adding a tag the change has, or removing one it doesn't, is a no-op.
  - **Archived changes:** allowed, since the endpoint doesn't check `ArchivedAt`.
- **"+ Add from the picker"** is two calls: `POST /api/projects/{id}/tags`, then the change call. If the second
  fails, the new tag stays in the Project's list, which is harmless and visible.
  - **Alternative rejected:** a combined endpoint. It would be atomic, but it's a second way to create tags with its
    own validation, for a failure nobody will notice.
- **Over MCP:** `tag_change` takes `{ project, repo: { host, owner, name }, change, add: [text], remove: [text] }`,
  and returns `{ tags: [text] }` with the change's project tags afterwards.
  - **Agents use text, not ids:** agents know text (from `list_projects`), so they never handle ids. Each text is
    parsed with D1 and looked up by `(KeyKey, ValueKey)`.
  - **Which change:** the Project's active change with that repo and name. With no active one, the most recently
    archived one.
  - **The checks:** role, visibility and tag existence are checked before anything is written, and the call changes
    nothing on any failure. The errors are listed in spec `tags` ("Agents only use existing tags"). An unknown tag's
    error lists the Project's tags in canonical text.
  - **A bootstrap-token reporter** (Development) may tag any change. It's treated as an owner, consistent with it
    getting every Project from `list_projects`.
- **`list_projects`** gains `tags: [text]` on each Project. Contracts: `ProjectSummary.Tags`, and new
  `TagChangeArguments`/`TagChangeResult`.

### D5. Personal tags in reports

- **The contract:** `ReportProgressArguments.PersonalTags: string[]?` (texts).
  - **`null`** (an older bridge) leaves the agent's set alone.
  - **`[]`** clears it.
  - **Otherwise:** each text is parsed and duplicates are collapsed. More than 20, or any malformed tag, refuses the
    whole report with an MCP error, before anything is recorded (like the document checks).
- **Recording:** `ReportRecorder` replaces that `ChangeAgent`'s `change_agent_tags` rows inside the report's
  transaction. This applies to every report, including the archive report, and after the archive nothing changes
  them, which is what freezes them.
- **A member's tags on a change** are the union of their agents' rows:
  `change_agent_tags ⋈ agents WHERE agents.member_id = viewer`.

### D6. Reads and the live stream, per viewer

- **The card carries both kinds:** `ChangeCard` gains `projectTags: [{ id, key, value }]` and
  `personalTags: [{ key, value }]`. History entries and `ChangePage` get them through their card.
- **Personal tags are filled per viewer** at read time:
  - **`ChangeCards`, `ChangePages` and the history** each take the `Viewer` and fill `personalTags` from that
    member's agents only.
  - **A bootstrap viewer** (Development) has no member, and sees no personal tags.
- **The live stream without leaking:**
  - **What's published:** the shared card goes out with `personalTags` empty, and `ChangeEvent.Updated` gains
    `PersonalTags: IReadOnlyDictionary<Guid, Tag[]>` keyed by member id. This map is server-side only and never
    serialised whole.
  - **What each subscriber gets:** `StreamAsync` writes `card with { PersonalTags = map[viewer.MemberId] or [] }`.
    It's one dictionary lookup per subscriber, with no database read, and another member's tags never reach the
    writer.
  - **Building the map:** the recorder builds it from the change's `change_agent_tags` when it publishes.
  - **Alternative rejected:** a separate `personal-tags` event per member. That would be a second event type that
    has to arrive in the right order relative to the card.
- **Tagging a change** republishes the card (`Updated`) the same way.
- **Renaming or deleting a Project tag** publishes `ProjectTagsChanged(projectId, tags)`. Each subscriber in that
  Project's scope gets `project-tags-changed { projectId, tags: [{ id, key, value }] }`. The web rewrites the
  `projectTags` of its cards in that Project from the list, renaming by id and dropping ids that are gone. Adding a
  tag sends it too, so open filter choices stay current.
- **Change page:** it's live on its card's `Updated` events already, and handles `project-tags-changed` the same way.

### D7. The Tags filter

- **In the address:** a repeatable `tag` parameter, in canonical order (sorted), after the other `FILTER_KEYS`:
  - `tag=t:<tagId>`: a Project tag;
  - `tag=k:<projectId>:<key>`: a Project key alone, any value;
  - `tag=p:<key>` or `tag=p:<key>:<value>`: the viewer's own personal tag, or their key alone.
- **Why these forms:** Project tags go by id, so renames don't break links (spec). A Project key goes by text, so
  renaming every `area:` tag to `zone:` breaks a `k:` link. That's acceptable: the filter then matches nothing and
  offers to clear, like a deleted tag. The value is the last part, so it can contain `:`, and it's URL-encoded.
- **Matching:** AND across every chosen tag. The other filters keep "one value replaces the old one". In `filters.ts`,
  `tags` becomes the one array-valued key, and `matches` checks every chosen tag.
- **On the server:** `HistoryFilter` gains `Tags: IReadOnlyList<TagFilter>`, with one `EXISTS` per tag:
  - **Project tag:** `change_project_tags` on the tag id.
  - **Project key:** `change_project_tags ⋈ project_tags` on Project id and `key_key`.
  - **Personal:** `change_agent_tags ⋈ agents` on `member_id = viewer`, `key_key`, and `value_key` when given.
  - **Who gets it:** the history and metrics endpoints parse repeated `tag` values, and refuse a malformed one with
    400, naming the parameter (as today). The metrics reuse the same `HistoryFilter`.
- **The choices:**
  - **The history:** `HistoryChoices` gains `tags`, built from visible archived changes, with personal tags from the
    viewer's own agents only.
  - **The dashboard:** builds its choices from its cards, whose personal tags are already the viewer's alone.
  - **The order:** both give personal tags first, then each Project by code, each group sorted by key then value. A
    key with any value also gets a key-only choice above its values.
- **Limits:** a URL with more than 20 `tag` values is refused with 400 on the server, and trimmed with "dropped" on
  the dashboard. That stops pathological query plans.

### D8. CLI

- **The config record:** `SavedLogin` becomes `CliConfig(ServerUrl, Token, Tags)`. `Tags` is a `string[]?` of
  canonical texts.
  - **Read-modify-write:** every writer (`login` and `tags add`/`remove`) reads the whole config and writes it back,
    so `panopticon login` keeps tags and `tags add` keeps the login. Same atomic temp-file write and 0600, as today.
- **The commands:**
  - **`panopticon tags`:** prints one tag per line, or "No personal tags." when there are none.
  - **`tags add <tag>…` / `tags remove <tag>…`:** each tag is parsed with D1. They exit 1 with the error on a
    malformed tag, a duplicate, the 21st tag, or removing one that isn't there, and change nothing.
  - **Help:** `panopticon --help` lists `tags`.
- **The bridge:**
  - **Reports:** each `report_progress` reads the config's tags just before forwarding (it already reads the login
    per call), and sends `PersonalTags` (`[]` when none). A malformed tag in a hand-edited file is a tool error naming
    the file and the tag, never the token.
  - **`tag_change`:** the bridge serves it, filling `project` from `.panopticon.json` and `repo` from the git remote
    when the agent leaves them out. It forwards like `list_projects`, and maps errors the same way.

### D9. Web

- **Design first:** the chips, the change-page picker, the Project page's Tags section and the multi-select filter are
  designed with the `frontend-design` skill and recorded in `web/DESIGN.md` before they're built (a process mandate).
- **The chips:**
  - **A shared component:** a `tagChips(card)` helper renders the project tags (with the code, reusing the
    `projectLabel` style) and then the personal tags. It's used by cards, history rows and the change page.
  - **No tags, no row:** a change with no tags renders no element.
- **The picker** (change page, owners and editors only): a searchable list of the Project's tags, with a check mark on
  the ones the change has. It ends with "+ Add `PAN/<typed>`" when the typed text parses and matches no tag. A parse
  error shows inline.
- **The Project page's Tags section:**
  - **Everyone:** sees the list, with each tag's change count.
  - **Owners and editors:** add, rename inline, and delete with a confirm that says how many changes carry the tag.
- **The filter:** one multi-select "Tags" control in `filterBar.ts`. Chosen tags show as removable chips. It works on
  the dashboard, history and metrics.

### D10. Contracts and compatibility

All of the following is additive on the wire:
- `ReportProgressArguments.PersonalTags`;
- `ProjectSummary.Tags`;
- the `tag_change` tool;
- the new card fields.

So an older bridge reports without tags, and an older server ignores a newer bridge's `personalTags`. The bridge's
`tag_change` against an older server fails with the server's "unknown tool" error, passed through.

## Risks / Trade-offs

- **[Personal tags reach a subscriber they shouldn't]:** the whole privacy promise rests on `StreamAsync` picking only
  its viewer's entry. Mitigation: a `StreamAsync` test with two members on one Project change asserts that Sam's
  stream never contains Emmz's tag text. An API sweep test asserts the same for every change-reading endpoint and
  `HistoryChoices`.
- **[`login` wipes the tags, or `tags` wipes the login]:** the config used to have one writer. Mitigation: D8's
  read-modify-write, with tests in both directions.
- **[Filter queries get slower]:** each chosen tag adds an `EXISTS`. Mitigation: primary keys lead with `change_id`,
  an index on `project_tags (project_id, key_key)`, and the 20-tag cap.
- **[`k:` links break on a key rename]:** accepted (D7). The page offers to clear, as it does for a deleted tag.
- **[A stale picker]:** an editor's picker can offer a tag another editor just deleted. The change call answers 400,
  and `project-tags-changed` has already refreshed the list by the time they retry.
- **[An agent tags the wrong change]:** `tag_change` falls back to the latest archived change with that name. That's
  a guess, but agents tag the change they're working on, and the result names the change's tags so a mistake shows
  up.

## Migration Plan

1. One EF migration adds `project_tags`, `change_project_tags` and `change_agent_tags`, with their indexes. The down
   migration drops them.
2. There's no data backfill. The server isn't live yet, and existing changes start untagged.
3. Rollback: the older server ignores the new tables. The older bridge's reports carry no `personalTags`, which
   leaves sets alone.

## Open Questions

None. All the product questions were settled with Emmz on 2026-11-10 (see docs/DISCOVERY.md, Tags).
