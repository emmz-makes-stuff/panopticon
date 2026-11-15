# repos Specification

## Purpose

The repos a member's changes come from: a page that shows each repo's active and archived changes together, and links
from every repo mention to the repo's own web page.

## Requirements

### Requirement: Repos list
A signed-in member SHALL be able to list the repos that have at least one change they can see (spec
change-visibility). Each repo SHALL show its host, owner and name. Under it SHALL come its active changes, most recently
reported first, then its archived changes, most recently archived first. Each change SHALL show its name as a link to
its change page, its Project's code (or that it's personal), and when it last reported or was archived.

#### Scenario: A repo with active and archived changes
- **WHEN** `emmz-makes-stuff/panopticon` has `repos-page` active and `visit-requests` archived on 2026-11-14
- **THEN** the Repos page shows the repo with `repos-page` first, then `visit-requests` with "Archived 14 Nov 2026",
  and each change name links to `/changes/<id>`

#### Scenario: Only visible changes count
- **WHEN** Sam can see no change in `cpointrepos/SMART-NOVA/NOVA-INFRA`
- **THEN** Sam's Repos page doesn't list that repo, and `GET /api/repos` doesn't return it to Sam

#### Scenario: Nothing reported yet
- **WHEN** the member can see no change at all
- **THEN** the Repos page shows an empty state saying repos will appear once an agent reports

#### Scenario: Signed out
- **WHEN** someone without a session opens the Repos page
- **THEN** the API answers 401, and the app shows the sign-in page and then returns to the Repos page

### Requirement: Active change progress on the Repos page
Each active change on the Repos page SHALL show its whole-change progress as a lit-cell bar with its count, as its card
does. Archived changes SHALL show no bar.

#### Scenario: An active change shows its progress
- **WHEN** `repos-page` has 7 of its 8 tasks done
- **THEN** its row shows a bar with 7 of 8 cells lit and "7/8 (88%)", and archived rows show no bar

### Requirement: Repos order
Repos SHALL be listed by their latest activity, newest first. A repo's latest activity is the latest of its active
changes' last reports and its archived changes' archive times. Repos with the same latest activity SHALL be ordered by
host, owner and name.

#### Scenario: Archived activity counts
- **WHEN** repo A's only change last reported on 2026-11-10 and repo B's only change was archived on 2026-11-12
- **THEN** repo B is listed before repo A

### Requirement: Archived changes on the Repos page
Each repo SHALL show at most its five most recently archived changes. A repo whose History lists more archived changes
than it shows SHALL link to History filtered to that repo, saying how many archived changes History will list for it,
retired copies included. A personal copy retired by linking (spec change-history, Retired copies
in the history) SHALL NOT appear on the Repos page.

#### Scenario: More than five archived
- **WHEN** a repo has eight archived changes and one retired copy
- **THEN** it shows the five most recently archived, then "All 9 in History", linking to
  `/history?repo=<host>/<owner>/<name>`

#### Scenario: A retired copy
- **WHEN** Emmz's personal `cleanup` was retired because `PAN` already had `cleanup`
- **THEN** the Repos page shows `PAN`'s `cleanup` under the repo, and not the retired copy, then "All 2 in History",
  since History lists both (Emmz, 2026-11-15)

### Requirement: Repo links
Wherever the dashboard shows a repo's owner and name (the cards, History rows, the change page, the metrics page's
slowest sections and the Repos page), that text SHALL link to the repo's web page, opening in a new tab. The repo
filter choices SHALL stay plain filters, with no link.

#### Scenario: A card's repo
- **WHEN** a card shows `github.com` and `emmz-makes-stuff/panopticon`
- **THEN** `emmz-makes-stuff/panopticon` links to `https://github.com/emmz-makes-stuff/panopticon` in a new tab, and the
  change name still links to the change page

### Requirement: Repo web addresses
A repo's web address SHALL be built from its stored host, owner and name over `https`, with the host lowercased. For
`dev.azure.com` and any host ending in `.visualstudio.com` it SHALL be `https://<host>/<owner>/_git/<name>`; the old
SSH host `vs-ssh.visualstudio.com` with owner `v3/<org>/<project>` SHALL use `<org>.visualstudio.com` and owner
`<project>`. For every other host it SHALL be `https://<host>/<owner>/<name>`. Each segment SHALL be URL-encoded.

#### Scenario: Azure DevOps
- **WHEN** the repo is `dev.azure.com`, owner `cpointrepos/SMART-NOVA`, name `NOVA-INFRA`
- **THEN** it links to `https://dev.azure.com/cpointrepos/SMART-NOVA/_git/NOVA-INFRA`

#### Scenario: A nested GitLab group
- **WHEN** the repo is `gitlab.com`, owner `team/platform`, name `api`
- **THEN** it links to `https://gitlab.com/team/platform/api`

#### Scenario: A segment that needs encoding
- **WHEN** the repo's name is `my repo`
- **THEN** the link's last segment is `my%20repo`

#### Scenario: A mixed-case Azure DevOps host
- **WHEN** a report gave the host as `Dev.Azure.com`
- **THEN** it links to `https://dev.azure.com/<owner>/_git/<name>`

#### Scenario: The old Azure DevOps SSH host
- **WHEN** the repo is `vs-ssh.visualstudio.com`, owner `v3/cpoint/SMART-NOVA`, name `NOVA-INFRA`
- **THEN** it links to `https://cpoint.visualstudio.com/SMART-NOVA/_git/NOVA-INFRA`

### Requirement: Repos that get no link
A repo whose host isn't a plain hostname, or whose owner or name has a `.` or `..` segment, SHALL get no link: its
owner and name SHALL show as plain text.

#### Scenario: A host that could open another site
- **WHEN** a report gave the host as `github.com@evil.example`
- **THEN** the repo's owner and name show as plain text, with no link

#### Scenario: A dot segment
- **WHEN** a report gave the name as `..`
- **THEN** the repo's owner and name show as plain text, with no link

### Requirement: Repos in the header
The header SHALL link to the Repos page, after History.

#### Scenario: Opening the Repos page
- **WHEN** a signed-in member chooses Repos in the header
- **THEN** the app shows `/repos` without reloading the page
