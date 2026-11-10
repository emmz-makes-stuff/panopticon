## ADDED Requirements

### Requirement: Personal tags in the config
The CLI config file SHALL hold an optional list of personal tags beside the saved login. `panopticon tags` SHALL list
them; `panopticon tags add <tag>…` and `panopticon tags remove <tag>…` SHALL edit them, keeping the saved login and
the file's permissions. A malformed tag, a duplicate or a 21st tag SHALL be refused, saying why, and change nothing.

#### Scenario: Adding a tag
- **WHEN** a member runs `panopticon tags add "language: csharp"` with `dev` already saved
- **THEN** `panopticon tags` lists `dev` and `language: csharp`, and the saved login is unchanged

#### Scenario: Removing a tag
- **WHEN** a member runs `panopticon tags remove DEV` with `dev` saved
- **THEN** `dev` is removed, since tags compare without regard to case

#### Scenario: A malformed tag
- **WHEN** a member runs `panopticon tags add "has space"`
- **THEN** it exits non-zero with an error naming the tag, and the file is unchanged

#### Scenario: A malformed file
- **WHEN** the config file's tags list holds a malformed tag, and the agent calls `report_progress`
- **THEN** the tool returns an error naming the config file and the tag, without the file's token, and the bridge
  keeps serving

### Requirement: Personal tags sent with reports
The bridge SHALL send the config's personal tags with every `report_progress`, read when it reports, so an edit takes
effect on the next report without a restart. With no tags saved, it SHALL send an empty list. The agent SHALL NOT be
asked for, or shown, personal tags.

#### Scenario: Next report picks up an edit
- **WHEN** the bridge is running, and the member runs `panopticon tags add urgent`
- **THEN** the agent's next report carries `urgent`

#### Scenario: Tags removed
- **WHEN** the member removes every personal tag
- **THEN** the next report carries an empty list, and the change loses that agent's tags

### Requirement: Tagging through the bridge
The bridge's `tag_change` SHALL forward to the server's `tag_change` with the same server and token as reports (see
Configuration). When the agent gives no Project id or repo, the bridge SHALL fill them from `.panopticon.json` and the
git remote, as for reports. Errors SHALL come back as tool errors, and the bridge SHALL keep running.

#### Scenario: An agent tags its change
- **WHEN** an agent in a repo linked to `PAN` calls the bridge's `tag_change` with change `walking-skeleton` and add
  `bug`
- **THEN** the server tags `PAN`'s `walking-skeleton` with `bug`, and the agent gets the change's project tags

#### Scenario: No link file
- **WHEN** an agent calls `tag_change` in a repo with no `.panopticon.json` and gives no Project id
- **THEN** the tool returns an error saying the repo isn't linked to a Project, so there are no project tags to set

#### Scenario: Refused by the server
- **WHEN** the server refuses the call because the tag doesn't exist
- **THEN** the agent gets the server's error, listing the Project's tags, and the bridge keeps serving
