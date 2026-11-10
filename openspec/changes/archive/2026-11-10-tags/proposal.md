# Proposal

## Why

Projects keep each team's work apart, but within a Project, or across your own work, there's no way to say what a
change *is*: a bug, onboarding work, C#. The filter bar can narrow by repo, person, machine and agent type. It can't
narrow by any dimension the team or a person chooses. Tags add that, and they give the `editor` role its job
(docs/DISCOVERY.md, "Later → Tags"; decided with Emmz, 2026-11-10).

## What Changes

- **What a tag is:** `key` or `key: value`, e.g. `bug` or `area: onboarding`.
  - **Keys:** letters, digits, `-`, `_` and `.`, matched without regard to case.
  - **Values:** any text without line breaks, trimmed.
  - **Limits:** up to 40 characters for each part. A malformed tag is refused with an error that says why, never
    silently dropped.
- **Project tags:** a Project defines a list of whole tags (up to 200). They're shown with the Project's code, as in
  `PAN/bug` or `PAN/area: onboarding`.
  - **The Project page:** a Tags section. Owners and editors add, rename and delete tags there. Viewers see the list.
  - **Renaming:** the tag changes everywhere it's used. A name that would collide with another tag is refused.
  - **Deleting:** after a confirm that says how many changes carry the tag, it comes off every change, archived ones
    included.
- **Tagging a change:** owners and editors put the Project's tags on its changes, active or archived, from a picker on
  the change page. The picker can also add a new tag to the Project's list ("+ Add `PAN/…`").
- **Agents tag changes too:**
  - **Seeing the tags:** `list_projects` now returns each Project's tags.
  - **Setting them:** a new `tag_change` MCP tool, on the server and the bridge. It adds and removes the Project's
    existing tags on a change, which it names by repo and change name.
  - **Who can:** only an owner's or editor's agent. A viewer's agent, an unknown tag or a change you can't see gets an
    MCP error that says what's wrong. For an unknown tag, the error lists the tags that exist.
  - **No new tags:** an agent can't create a tag.
- **Personal tags:** up to 20, kept in the CLI config (`config.json`, next to the login).
  - **Managing them:** `panopticon tags`, `panopticon tags add "language: csharp"` and `panopticon tags remove dev`
    list and edit them.
  - **Reporting them:** the bridge sends them with every report, and the agent never sees or chooses them.
    `report_progress` gains an optional `personalTags` field.
  - **On a change:** each agent's latest set counts, and a change has the union of your agents' sets. Only you see
    them. They stay as they were once the change is archived.
- **Seeing tags:** chips on cards, change pages and history rows, the project tags first, then yours. Untagged changes
  look as they do today.
- **Live:** editing a change's project tags updates every member's open dashboard and change page. Personal tags reach
  only their owner. Project tag renames and deletes reach all of that Project's members.
- **A Tags filter** on the dashboard, the history and the metrics:
  - **Several tags at once:** it's the first filter that takes more than one value, and the chosen tags combine with
    AND.
  - **Key alone:** choosing `language` matches any `language: …` tag.
  - **The choices:** your personal tags, then each Project's tags, from the changes on that page. Nobody else's
    personal tags are ever offered.
  - **In the address:** project tags go by id, so renaming a tag doesn't break a link.
- **Docs:** `docs/DISCOVERY.md` is corrected. Its 2026-11-10 note says reports don't carry project tags, but agents
  now set them through `tag_change` (separately from reports).

Not in this change: editing personal tags on the dashboard, tag colours, tags defined server-wide, and agents creating
Project tags.

## Capabilities

### New Capabilities

- `tags`: the tag syntax and limits, a Project's tag list, putting project tags on changes (dashboard and
  `tag_change`), personal tags and how they're combined, who sees which tags, and how tags appear on cards, change
  pages and history rows.

### Modified Capabilities

- `projects`: the Project page gains a Tags section that owners and editors can edit, `list_projects` returns each
  Project's tags, and the editor role is now defined by what it does.
- `progress-reporting`: `report_progress` takes optional `personalTags`, which are validated, and the reporting
  agent's set is replaced.
- `cli-bridge`: the `panopticon tags` commands, personal tags in the config and in every report, and the bridge
  serving `tag_change`.
- `change-filters`: the Tags filter (multi-valued, AND, key-only matching, ids in the address, scoped choices), on the
  dashboard and the history. "Choosing a new value replaces the old one" is relaxed for this one filter.
- `change-metrics`: the metrics page takes the Tags filter.
- `live-dashboard`: tag edits update open dashboards and change pages live. How tags look on cards is covered by
  `tags`.

## Impact

- **Server:**
  - **New data:** `project_tags` (id, Project, key, value, with a case-insensitive unique key) and
    `change_project_tags` (change, tag). `change_agents` gains its latest personal tag set. There's a migration.
  - **New endpoints and tools:** the Project tag endpoints (list, add, rename, delete), the change tag endpoints, and
    the `tag_change` MCP tool, with the role checks reusing the Project membership ones.
  - **Reads:** `ChangeCards`, `ChangePages`, the history reader and `MetricsReader` return the tags and apply the
    filter. Personal tags are worked out per viewer.
  - **The live stream:** a card's personal tags now depend on who's viewing, so the stream adds each subscriber's own
    tags to the cards it sends. A rename or a delete is sent to the Project's members.
- **Contracts:** `ReportProgressArguments` gains an optional `personalTags`. The list-projects result gains `tags`,
  and there are new tag-change contracts. All of this is additive on the wire, so an older bridge still reports,
  without tags.
- **CLI:** the `panopticon tags` subcommands, the config file's `tags` field, the bridge sending `personalTags`, and
  the bridge forwarding `tag_change`. `docs/cli.md` documents them.
- **Web:** tag chips on cards, change pages and history rows, the change page's tag picker, the Project page's Tags
  section, and the Tags filter (multi-select) in the filter bar on the dashboard, history and metrics. The design is
  done with the `frontend-design` skill and recorded in `web/DESIGN.md`.
- **Docs:** the README (tags, who sees which) and `docs/DISCOVERY.md`.
- **No new dependencies.**
