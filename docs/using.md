# Using Panopticon

How members sign in, organise work into Projects and tags, connect their agents, and read the dashboard, change
pages, history and metrics. Connecting an agent in more depth is in [`cli.md`](cli.md).

## The first sign-in

Members come from `Panopticon:InitialMembers` at first. Everyone else is invited by email address, which lets them
sign in. A Project's owner can invite people to that Project (see [Projects](#projects)). An initial member can
also invite people to the server itself from the Members page, in no Project. They get an email that links to the
sign-in page.

1. On the sign-in page, enter an initial member's address (`dev@localhost.test` locally), and choose **Send me a
   link**.
2. Open the link. With no Resend key (Development), there's no email. The server logs the message as a warning
   instead, starting `Development: not sending email to dev@localhost.test`, and the link ends in
   `/sign-in/confirm#…`. Otherwise it's in your inbox, and it works once, for 15 minutes.
3. Choose **Sign in**. Opening the link alone doesn't sign you in, so a mail scanner that follows it can't.

Rename yourself on the Members page: the name appears on the cards next to every agent you run.

## Projects

A Project groups work and people. It has a name, a code such as `PAN` (2 to 10 letters or digits, starting with a
letter, which can't change once it's made) and an id. Cards, change pages and history rows show the code, and every
page with filters can narrow to one Project or to your personal work.

- **Making one:** anyone signed in can create a Project on the Projects page, and becomes its owner.
- **Roles:** each member of a Project is an **owner**, an **editor** or a **viewer**. Every role sees the Project's
  work and can report into it. Owners also rename it and manage its members: they invite an email address with a
  role, change roles and remove people. Owners and editors also manage the Project's tags (see [Tags](#tags)). A
  Project always keeps at least one owner, so its last owner can't step down, be removed or leave.
- **Joining:** inviting an address that isn't on the server yet also lets it sign in. No invitation email is sent,
  so tell them yourself. Anyone can leave a Project from its page.

**Linking a repo.** A repo's reports go to a Project when the repo has a `.panopticon.json` at its root naming the
Project's id:

```json
{
  "project": "6f1c2e4a-…"
}
```

Copy it from the Project's page, or ask your agent to link the repo: the bridge's `list_projects` tool gives your
Projects' ids. Commit the file, so everyone working on the repo reports into the same Project. Linking moves your own
active personal changes in that repo into the Project, with their history and timings. If a teammate already linked
the same change, yours is retired to your history as "Linked to PAN". More is in
[`docs/cli.md`](cli.md#linking-a-repo-to-a-project).

**Who sees what.** You see a change when it's in a Project you belong to, in any role, or when it's your own personal
work. A change from a repo with no `.panopticon.json` is personal, and only its reporter sees it. That holds on the
dashboard and its live updates, change pages and documents, the history and the metrics. A change you can't see
answers as though it didn't exist. Membership changes take effect on open pages at once. When you leave a Project or
are removed from it, its cards leave your dashboard, and an open page for one of its active changes says it wasn't
found. An archived change's page catches up on its next load. When you join a Project, its cards appear. The Members
page lists the people who share a Project with you. (In Development,
a report made with the bootstrap token has no member, so a change it makes in no Project is visible to everyone.)

**Inviting someone to the server, and removing them,** is for the initial members (`Panopticon:InitialMembers`) only.
Their Members page lists every member of the server, not just those who share a Project. Removing someone ends their
sessions, revokes their tokens and takes them out of every Project. Their past reports stay. If they're the last
owner of a Project, the removal is refused until someone else there is made an owner.

## Tags

A tag is a label to filter by: a key, with a value if you want one, such as `bug` or `area: onboarding`. A key is 1 to
40 letters, digits, `-`, `_` or `.`, and a value is up to 40 characters. Tags compare without regard to case. There are
two kinds:

- **A Project's tags**, such as `PAN/bug`, are shared. Each Project keeps a list of up to 200, and every member sees
  them.
  - **Managing them:** owners and editors add, rename and delete them in the Tags section of the Project's page.
    Viewers see the list, with how many changes carry each tag.
  - **Renaming or deleting:** a rename shows everywhere at once, on active and archived changes, and keeps filters
    and links working. Deleting asks first, saying how many changes carry the tag, then takes it off all of them.
  - **Tagging a change:** owners and editors use **Edit tags** on its change page, and can add a new tag to the
    Project from there. Agents can do it with the `tag_change` MCP tool, but only with the Project's existing tags,
    and only for an owner's or editor's token. See [`docs/cli.md`](cli.md#tagging-a-change).
- **Your personal tags**, such as `dev`, are yours alone. They're kept on each machine with
  `panopticon tags add dev "language: csharp"`, and every report from that machine carries them. A change has the
  personal tags of the latest report from each of your agents on it. Only you see them, even on a Project's changes.
  They're shown with a dashed border. See [`docs/cli.md`](cli.md#personal-tags).

Cards, change pages and history rows show a change's Project tags, then your personal ones, and tag edits show on
open pages at once. The dashboard, the history and the metrics each have a **Tags** filter. A change shows only when
it has every tag you choose, and choosing a key alone (`language`, any value) matches any of its values. The tags go
in the address, so a filtered view can be shared. Project tags go by id there, so renaming one keeps the link working.

## Reporting progress

The **Set up a machine** guide takes you through it, on the Tokens page and, until your first report, on the Projects
page. Each step shows as done once your tokens show it. On the machine where your agent works:

1. **Install the CLI.** The guide shows the command for your OS (`curl -fsSL <server>/install.sh | sh` or
   `irm <server>/install.ps1 | iex`), and `panopticon update` keeps it in step with the server later. A server built
   locally has no CLI builds, so build and install it from source with `make install` instead (it puts `panopticon`
   in `~/.local/bin`; set `INSTALL_DIR` for elsewhere).
2. **Log in**, which gives this machine a token:

   ```sh
   panopticon login --server https://panopticon.example.com
   ```

   Use your server's address (`http://localhost:7480` for a local one).

   It prints a code and a link. Open the link while signed in, check the code, and choose **Approve**. The CLI saves
   the token, and `panopticon mcp` uses it from then on.
3. **Connect your agent.** In Claude Code, add the Panopticon plugin, then check the setup:

   ```text
   /plugin marketplace add emmz-makes-stuff/panopticon-skills
   /plugin install panopticon@panopticon-skills
   /panopticon:setup
   ```

   The plugin starts the bridge with the saved login, so the commands are the same for every server. Other agents,
   and the `.mcp.json` route, are in [`docs/cli.md`](cli.md#claude-code).

To have a repo's reports go to a Project, ask your agent to link the repo to it, or commit the `.panopticon.json`
from the Project's page. The Tokens page lists every machine's token, and revoking one stops its reports at once.

## Change pages and the history

Each report sends the whole change folder: `proposal.md`, `design.md`, `tasks.md`, the specs, the devlog and any
other files. Choose a change's name on its card to open its page. The page lists those documents, grouped, and
shows one at a time, rendered from Markdown. It opens on the proposal, and it updates live while the change is
active.

When `/opsx:archive` moves the folder to `openspec/changes/archive/`, a running `panopticon mcp` that reported the
change notices, and reports it as archived. The card leaves the dashboard, and the change appears on the History
page, newest first, with its documents as they were when it was archived. If no bridge was running, the next report
of that change name archives it instead. How the bridge finds archives is in [`docs/cli.md`](cli.md#archives).

## Metrics

The server keeps the progress each report saw, so it can say how long changes and their sections take. Every time is
**wall-clock time**: a change parked over a weekend counts the weekend.

- **Time to archive:** a change's time from its first report to its archive. While the change is active, its page
  shows its time so far ("Running for 2d").
- **Time per section:** for each numbered section of `tasks.md` (`## 2. Core`), the time from its start to its
  finish.
  - **Start:** the first report in which the section has a ticked task, or is the current section.
  - **Finish:** the first report from which every one of its tasks stays ticked, through the change's last report.
    A section that's reopened, by unticking a task, finishes again later.
  - **Not measured:** a section that's started but not finished is in progress, with its time so far. Once the
    change is archived, it's unfinished, with its time up to the archive. A section
    never started, or with no tasks, has no time. Sections without a number aren't measured.
  - **Matching:** sections are matched by their number, and show their latest title. A change that renumbers its
    sections part-way joins different sections under one number.

A change's page shows its time and each section's. The **Metrics** page (in the header) covers archived changes,
filtered like the history:
- the number of changes, their median time to archive, and the median time per (finished) section;
- a chart of the weekly median time to archive over the last 12 weeks, in Monday-start weeks (UTC) by archive date;
- the ten slowest sections, each linking to its change.
