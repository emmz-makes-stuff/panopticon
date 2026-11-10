# tags Specification

## Purpose

Tags let a team and each person mark what a change is, such as `bug`, `area: onboarding` or `language: csharp`,
and narrow their views by it. Project tags are shared by a Project's members, and personal tags are one person's own.

## Requirements

### Requirement: Tag syntax
A tag SHALL be a key, optionally followed by `:` and a value, such as `bug` or `area: onboarding`. A key SHALL be 1 to
40 letters, digits, `-`, `_` or `.`. A value SHALL be 1 to 40 characters with no line breaks, trimmed. The first `:`
SHALL split key from value. Keys and values SHALL be compared without regard to case, and keep the casing first given.

#### Scenario: A key and value
- **WHEN** a tag `Area:  Onboarding ` is given
- **THEN** it's read as key `Area` and value `Onboarding`, and equals `area: onboarding`

#### Scenario: A colon in the value
- **WHEN** a tag `link: http://x` is given
- **THEN** its key is `link` and its value is `http://x`

#### Scenario: Malformed tags refused
- **WHEN** a tag is `has space`, `: value`, `key:` or has a 41-character key
- **THEN** it's refused with an error naming the tag and saying what's wrong, and nothing is changed

### Requirement: A Project's tags
Each Project SHALL have a list of up to 200 tags, each with its own id. No two tags in a Project SHALL be equal. A
Project tag SHALL be shown with the Project's code, such as `PAN/bug`. Every member of the Project SHALL see its tags.

#### Scenario: Shown with the code
- **WHEN** `PAN` has the tag `area: onboarding`
- **THEN** it's shown as `PAN/area: onboarding`

#### Scenario: The limit
- **WHEN** an editor adds a tag to a Project that already has 200
- **THEN** it's refused, saying the Project has the most tags it can have

### Requirement: Managing a Project's tags
A Project's owners and editors SHALL be able to add, rename and delete its tags. Viewers SHALL NOT. Adding or renaming
to a tag equal to another of the Project's tags SHALL be refused, naming the existing tag.

#### Scenario: An editor adds a tag
- **WHEN** an editor of `PAN` adds `bug`
- **THEN** `PAN/bug` is in `PAN`'s tags

#### Scenario: A viewer can't
- **WHEN** a viewer of `PAN` tries to add, rename or delete a tag through the API
- **THEN** it answers 403 and nothing changes

#### Scenario: A duplicate
- **WHEN** `PAN` has `area: onboarding`, and an editor renames `area: billing` to `Area: Onboarding`
- **THEN** it's refused, naming `PAN/area: onboarding`

### Requirement: Renaming a tag
Renaming a Project tag SHALL change it everywhere it appears, on active and archived changes, and SHALL NOT break
links or filters that name it.

#### Scenario: Renamed on its changes
- **WHEN** `PAN/area: onboarding` is on three changes, and an editor renames it to `area: first-run`
- **THEN** those three changes show `PAN/area: first-run`

#### Scenario: Filter link survives
- **WHEN** a dashboard address filters by that tag, and it's renamed
- **THEN** the address still filters by it, and the filter shows its new name

### Requirement: Deleting a tag
Deleting a Project tag SHALL remove it from every change that carries it, active and archived, and from the Project's
list. Before deleting, the dashboard SHALL ask for confirmation, saying how many changes carry the tag.

#### Scenario: Confirm with a count
- **WHEN** an editor deletes `PAN/bug`, which is on 12 changes
- **THEN** the page asks "`PAN/bug` is on 12 changes. Delete it?", and only a confirm deletes it

#### Scenario: Gone from changes
- **WHEN** `PAN/bug` is deleted
- **THEN** no change shows it, and the Tags filter no longer offers it

### Requirement: Tagging a change on the dashboard
A Project's owners and editors SHALL be able to add and remove the Project's tags on any of its changes, active or
archived, from the change page. Viewers SHALL NOT. A personal change SHALL have no project tags.

#### Scenario: An editor tags a change
- **WHEN** an editor of `PAN` adds `PAN/bug` to change `walking-skeleton` on its page
- **THEN** the change shows `PAN/bug`

#### Scenario: An archived change
- **WHEN** an editor adds `PAN/bug` to an archived change from its page
- **THEN** its history row and page show `PAN/bug`

#### Scenario: A viewer can't
- **WHEN** a viewer of `PAN` opens a change page
- **THEN** it shows the change's tags with no way to edit them, and the API answers 403 if asked to

### Requirement: Adding a tag from the picker
The change page's tag picker SHALL let an owner or editor add a new tag to the Project's list and to the change in one
step, when no tag in the list equals what they typed.

#### Scenario: A new tag from the picker
- **WHEN** an editor types `area: billing` in the picker, and `PAN` has no such tag
- **THEN** the picker offers "+ Add `PAN/area: billing`", and choosing it adds it to `PAN`'s tags and to the change

### Requirement: Tagging a change over MCP
The server's MCP endpoint and the bridge SHALL offer a `tag_change` tool. It SHALL take a Project id, a repo, a change
name, and lists of tags to add and to remove, and SHALL return the change's project tags afterwards. Adding a tag the
change has, or removing one it lacks, SHALL change nothing and not be an error.

#### Scenario: An agent tags its change
- **WHEN** Emmz's agent, an editor of `PAN`, calls `tag_change` for `walking-skeleton` with add `bug`
- **THEN** the change has `PAN/bug`, and the result lists the change's project tags

#### Scenario: Add and remove at once
- **WHEN** a change has `PAN/bug`, and an agent calls `tag_change` with add `area: onboarding` and remove `bug`
- **THEN** the change has `PAN/area: onboarding` and not `PAN/bug`

### Requirement: Agents only use existing tags
`tag_change` SHALL be refused, changing nothing, when: the reporter's member is not an owner or editor of the
Project; the change is not in that Project or is not visible to them; or a tag is not in the Project's list. The
error SHALL say which. For an unknown tag, it SHALL list the Project's tags.

#### Scenario: A viewer's agent
- **WHEN** Sam, a viewer of `PAN`, has an agent call `tag_change`
- **THEN** it gets an error saying Sam's role in `PAN` can't tag changes, and nothing changes

#### Scenario: An unknown tag
- **WHEN** an agent calls `tag_change` with add `bugg`, and `PAN` has `bug` and `area: onboarding`
- **THEN** it gets an error naming `bugg` and listing `bug` and `area: onboarding`, and nothing changes

#### Scenario: No such change
- **WHEN** an agent calls `tag_change` for a change name `PAN` doesn't have
- **THEN** it gets an error saying the change wasn't found in `PAN`

### Requirement: Personal tags on a change
A change SHALL have, for each member, the personal tags in the latest report from each of that member's agents on it,
combined. An agent's latest report SHALL replace that agent's set, so a tag removed from every agent's set leaves the
change. Personal tags SHALL stay as they were once a change is archived.

#### Scenario: Two machines
- **WHEN** Emmz's `vm-1` reports with `dev`, and her laptop reports on the same change with `urgent`
- **THEN** the change has `dev` and `urgent` for Emmz

#### Scenario: A tag removed
- **WHEN** `vm-1` then reports with no personal tags, and the laptop's set is still `urgent`
- **THEN** the change has only `urgent` for Emmz

#### Scenario: Archived
- **WHEN** a change is archived with `dev` for Emmz
- **THEN** it keeps `dev` for Emmz in the history

### Requirement: Personal tags are private
A member's personal tags SHALL be visible only to that member, everywhere tags appear: cards, change pages, history
rows, the live stream, the API and filter choices.

#### Scenario: Sam doesn't see Emmz's tags
- **WHEN** Emmz and Sam both work on a `PAN` change, and Emmz's agents report `dev`
- **THEN** Emmz sees `dev` on the card, and Sam doesn't, nor in Sam's Tags filter choices

### Requirement: Tags shown on changes
Cards, change pages and history rows SHALL show a change's tags as the viewer sees them: the project tags first, with
the Project's code, then the viewer's personal tags. A change with no tags SHALL look as it did before tags.

#### Scenario: Both kinds
- **WHEN** a `PAN` change has `PAN/bug`, and Emmz's agents report `dev` on it
- **THEN** Emmz's card shows `PAN/bug` then `dev`

#### Scenario: No tags
- **WHEN** a change has no tags
- **THEN** its card shows no tag row
