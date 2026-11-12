# Dashboard design: "lit cells"

The brief (docs/DISCOVERY.md) is **fun but clean**, a glance that replaces SSH, and stalls obvious at once.

A panopticon is a round tower from which one watcher sees every cell. On the dashboard, each change is a
block of cells and each task in `tasks.md` is one cell. A finished task is a **lit cell**. That's the one
bold idea, and everything around it stays quiet.

## Name

The site title and wordmark are **"PANOPTICON"**, in capitals, with no article. Use it exactly so in the
wordmark, the page `<title>` and anywhere else the product is named in the UI. The wordmark is set in
Gabarito 700 with `letter-spacing: 0.03em` to open up the capitals. (This is a deliberate exception to "no
all-caps", because it's the name.) The logo always comes just before the wordmark (see "Logo").

## Logo

The logo is an eye in a triangle, glowing pale teal on a dark rounded tile (`#121929`). The source is
`docs/images/logo.webp` (1024×1024). The PNGs below are made from it once and committed. How they were made, the
script included, is in the `logo` change's devlog.

| File                              | Size     | Corners                     | Use                                    |
| --------------------------------- | -------- | --------------------------- | -------------------------------------- |
| `web/public/logo.png`             | 128×128  | transparent                 | header, before the wordmark            |
| `web/public/favicon-32.png`       | 32×32    | transparent                 | browser tab (`<link rel="icon">`)      |
| `web/public/apple-touch-icon.png` | 180×180  | filled with the tile colour | home screens (iOS rounds the corners)  |
| `docs/images/logo.png`            | 512×512  | transparent                 | README                                 |
| `docs/images/social-preview.png`  | 1280×640 | full-bleed tile colour      | GitHub social preview (a repo setting) |

- Corners are transparent everywhere except the touch icon and the social preview, so the tile never shows a white
  or square edge on either colour scheme.
- In the header the logo is `1.3em` tall (about 40px on the 31px wordmark), with a `0.3em` gap. It's lowered by
  `0.3em`, which centres it on the capitals. It stays inline, not flex, so the header row still lines up on the
  wordmark's baseline.
- It's decorative (`alt=""`) and sits inside the wordmark's link home, so a screen reader hears "PANOPTICON" once.
- The tile is dark on both schemes. On light paper it reads as an app icon. On dark paper its edge almost disappears
  and the glow carries it.

## Principles

1. **Lit cells are the memorable thing.** Progress bars are made of one segment per task: lit when done,
   unlit when not. They're discrete because checkboxes are discrete. Nothing else on the card competes
   with them for colour.
2. **Type carries hierarchy, not boxes.** Cards have a hairline border, a small radius and no shadow. The
   change name is the loudest text on the card.
3. **Stalls dim, they don't shout.** An idle card loses contrast and says how long it has been quiet. The
   eye goes to whatever is live and bright.
4. **One moment of motion.** When a report changes a card, the newly lit cells light in sequence, left to
   right, about 60 ms apart. That shows exactly what changed. There's nothing else: no entrance animations
   and no hover effects. `prefers-reduced-motion` turns it into an instant change.
5. **Plain words.** Sentence case, and no all-caps labels (the name is the one exception) or middot-joined meta. Counts read "4/5 (80%)", and agents read `claude-code@vm-1` (both as the live-dashboard spec words them).

## Colour tokens

Light is the default, and dark follows `prefers-color-scheme`. Indigo stands in for black, and the paper
is cool rather than cream.

| Token         | Light     | Dark      | Use                                                 |
| ------------- | --------- | --------- | --------------------------------------------------- |
| `--paper`     | `#F4F6FB` | `#12152B` | page background                                     |
| `--card`      | `#FFFFFF` | `#1B1F3B` | card surface                                        |
| `--ink`       | `#1C2140` | `#E9ECF8` | primary text                                        |
| `--ink-muted` | `#5B6180` | `#9CA3C4` | meta text (≥ 4.5:1 on card)                         |
| `--rule`      | `#DDE2EE` | `#2D3355` | card border, unlit cells                            |
| `--lit`       | `#0F8B7D` | `#3FD1BE` | lit cells, the only progress colour (≥ 3:1 on card) |
| `--spark`     | `#D6336C` | `#FF7AA2` | the live dot, and a just-updated card's edge        |
| `--prompt`    | `#6E56CF` | `#A594F9` | the `❯` terminal mark on session rows, nothing else |

- Unlit cells use `--rule` with an inset outline, and lit cells are solid `--lit`. The two differ in fill
  _and_ outline, and the counts are printed beside the bar, so state never relies on colour alone.
- Stale cards recede and their lit cells become `--ink-muted` (see "Filters and quiet cards": no opacity, so text
  keeps its contrast).

## Type

- **Gabarito** (geometric, friendly, slightly rounded) for the wordmark and change names: weight 700.
- **Atkinson Hyperlegible Next** for everything else: repo, agents, counts and times. Weights 400 and 600,
  with `font-variant-numeric: tabular-nums` on counts.
- The scale is a 1.25 ratio from a 16px base: 12.8 / 16 / 20 / 25 / 31. Line-height is 1.5 for body and
  1.15 for change names. Change names are 25px, and the wordmark is 31px.
- **Fonts are self-hosted** through the Fontsource npm packages (`@fontsource/gabarito`,
  `@fontsource/atkinson-hyperlegible-next`), so Vite bundles the `.woff2` files into `wwwroot`. There are no
  third-party requests, and it works offline. (Emmz, 2026-11-08.)

## Layout

```
◬ PANOPTICON              ● Live  3 changes   Emmz  Members  Tokens  Sign out
──────────────────────────────────────────────────────────────────────────
┌──────────────────────────┐ ┌──────────────────────────┐ ┌─────────
│ github.com               │ │ dev.azure.com            │ │ (dimmed)
│ emmz/panopticon   2m ago │ │ acme/Nova       14m ago  │ │ …  3h ago
│ walking-skeleton         │ │ add-auth                 │ │
│                          │ │                          │ │
│ Section 6: CLI bridge    │ │ Section 1: Tokens        │ │
│ ▮▮▮▮▯         4/5 (80%) │ │ ▮▯▯           1/3 (33%) │ │
│ Whole change             │ │ Whole change             │ │
│ ▮▮▮▮▮▮▮▮▮▯▯▯ 22/30 (73%)│ │ ▮▯▯▯▯▯▯▯▯     1/9 (11%) │ │
│ claude-code@vm-1 Emmz    │ │ cursor@laptop Sam        │ │
└──────────────────────────┘ └──────────────────────────┘ └─────────
```

- Left-aligned throughout. Cards sit in a grid with `repeat(auto-fill, minmax(20rem, 1fr))` and a 16px gap.
  A single column on phones, with 16px side gutters.
- **Card order, top to bottom:** host (small, muted), then owner/name with the time at the right, then the
  change name (the largest text), then the section bar and the whole-change bar, then the agents.
- **Bars:** cells are 10px tall with 3px gaps and a 2px radius, each with a flexible width. Above 40 tasks,
  the gaps disappear and the cells merge into a continuous fill, so very long changes don't turn into
  noise.
- **Empty bar** (no tasks): a dashed `--rule` track reading "No tasks in tasks.md yet". There's no 0%.
- **Agent row:** each agent is a hairline pill reading `claude-code@vm-1`, then the member who runs it in
  `--ink-muted` (`.card-agent-member`, design D17). The member is quieter than the agent, and absent for
  reports made before team sign-in.
- **Header:** the logo (`◬` above), then the wordmark. Both are inside the link home, which keeps its name styling
  (no underline, no link colour), and the logo is decorative. On the dashboard, the live state follows, pushed right: a `--spark` dot and "Live", or a
  hollow dot and "Offline, reconnecting…" while disconnected, with the number of changes beside it. Other
  pages leave that slot empty. When signed in, the account sits last: your display name (600), then
  Projects, Members, History, Metrics, Tokens and Sign out as small muted links with a `--rule` underline. On phones the header wraps,
  with the account on its own line.
- **Empty dashboard:** "No agents have reported yet." Below it, one line: connect one with the panopticon
  CLI, then ask it to report progress on a change. Then the two commands in a copyable block:
  `panopticon login --server <this origin>` and `claude mcp add panopticon --scope local -- panopticon mcp`.

## Pages: sign-in, members, tokens and device approval

These are tools, not views of progress, so they stay quieter than the cards and never use `--lit`.

```
Members
Everyone who shares a Project with you.
──────────────────────────────────────────────
Emmz (you)      emmz@example.com   Joined 8 Nov 2026
  Your display name [Emmz            ] [Save]
──────────────────────────────────────────────
Sam             sam@example.com    Joined 9 Nov 2026     [Remove]
──────────────────────────────────────────────
To invite someone, open one of your Projects. Its owners can invite people from there.
```

- **Members:** there's no invite form, since people join through a Project. Instead there's one muted line
  with a link to `/projects`. Remove appears only for initial members (`canRemoveMembers` on the session),
  and its confirmation says the person leaves every Project.
- **Tokens: the setup guide.** Below the token list sits the machine setup guide (see "Setup guide" below). It
  replaces cli-install's "Install the CLI" panel; the install step keeps that panel's tabs, command block and notes.

- **One left-aligned column**, 38rem wide, under the header. The page heading is Gabarito 700 at 25px, the
  same as the empty state's. Subheadings ("New Project", "Invite someone") are Gabarito at 20px. Lead text is
  `--ink-muted`.
- **Lists, not cards:** members and tokens are rows between hairline `--rule` dividers. The name (or the
  machine) is 600, and the email, dates and last use follow in small muted text. Cards stay reserved for
  changes.
- **Forms:** a label above each input (small, 600). Inputs and their button sit on one line and wrap on
  phones. Inputs have a 4px radius and a border dark enough to meet 3:1.
- **Buttons:** the main action of a page (Send me a link, Sign in, Create Project, Invite, Continue, Approve) is solid
  `--ink` with `--card` text. Teal stays the progress colour only. Others are outlined. Remove and Revoke
  are small outlined buttons at the row's right: the confirmation that follows does the warning, so they
  don't shout. Sign out and "Use a different address" are link buttons.
- **Errors** are `--ink` at 600 with a 3px `--spark` rule on the left, in a `role="alert"` line that takes
  no space while it's empty. They say what happened and what to do, and never apologise.
- **Device approval:** the heading names the machine ("Log in vm-1?"). The code is the loudest thing on
  the page, in Gabarito 700 at 31px with open letter-spacing, because it's what you check against the
  terminal. Approve and Deny sit side by side below a sentence saying whose token it makes. Nothing is
  decided on load.
- **Sign-in confirm:** "Sign in as emmz@example.com?" when this browser asked for the link, otherwise
  "Sign in to PANOPTICON?" (the server can't say whose link it is before the confirm). One "Sign in"
  button. A used or expired link explains which, with a "Send me a new link" link.

## Setup guide

"Set up a machine" takes a new member from nothing to a reporting agent, in three steps (spec machine-setup). It's a
tool, so it keeps the quiet voice of the Tokens page. It has one idea of its own: **each step's number sits in a cell**.

```
Set up a machine                                         Hide   (Projects only)
Run these on the machine where your agent works, then its reports show up here.
[1]  Install the CLI
 |   [macOS and Linux][Windows]
 |   $ curl -fsSL https://panopticon.example.com/install.sh | sh
 |   [Copy]  Copied.
 |   Installs panopticon 0.2.0.
[2]  Log in
 |   Log the machine in to this server. It opens a page here to approve it, and saves the login.
 |   $ panopticon login --server https://panopticon.example.com
 |   [Copy]
[✓]  Connect your agent   Done
     In Claude Code, add the Panopticon plugin, then check the setup. …
     > /plugin marketplace add emmz-makes-stuff/panopticon-skills     [Copy]
     > /plugin install panopticon@panopticon-skills                   [Copy]
     > /panopticon:setup                                              [Copy]
     ▸ Other agents   (a disclosure: the .mcp.json block, with Copy)
```

- **Cells, not circles.** A step's mark is a 1.6rem square with a 3px radius, drawn like an unlit progress cell: `--rule`
  fill, an inset outline, and the number in `--ink-muted`. A done step's cell is solid `--ink` with a check. It isn't
  `--lit`, because teal belongs to progress bars alone (principle 1). A small muted "Done" follows the title, so state
  never relies on the cell alone.
- **A hairline rail** in `--rule` joins the cells, because the steps really are a sequence. The last step has none.
- **Done steps don't dim.** Their commands stay at full contrast, because setting up a second machine is normal.
  Dimming is for stalls (principle 3).
- **Commands** use the bordered block of the link file and the install command, with a CSS-drawn prompt that never
  copies and that screen readers skip: `$` for the shell and `>` for Claude Code. The `.mcp.json` has no prompt. Each
  block has its own Copy and status line.
- **Type:** the title is Gabarito 700 at 20px, as the old panel's was. Step titles are Atkinson 600 at 16px. Body lines
  are muted.
- **Hide** (Projects only) is a plain text button at the right of the title row. There's no "show again": the Tokens
  page always has the guide.
- **No motion.** Marking a step done is an instant change on load.

## Projects

A Project's code is the number on its cell's door. The tower has many cells, and the plate says whose is whose.
That gives the Projects pages their one deliberate thing, the **code plate**. Everything else is in the tools'
quiet voice, as above.

For a first run, the Projects list page opens with the setup guide (see "Setup guide"), above the list, with Hide. It
shows until one of your tokens has been used, or until it's hidden in this browser.

```
Projects
──────────────────────────────────────────────
┌─────┐
│ PAN │  Panopticon                     Owner
└─────┘
──────────────────────────────────────────────
┌──────┐
│ ACME │ Acme client work               Viewer
└──────┘
──────────────────────────────────────────────

New Project
Name [                ]  Code [      ]  [Create Project]
                         2 to 10 letters or digits, starting with a letter.
```

```
All Projects
┌─────┐
│ PAN │                    (the plate at 31px: the loudest thing on the page)
└─────┘
Panopticon                 (h2)
You're an owner. Id 6f1c2e4a-…   (small, muted; the id is selectable)
Name [Panopticon      ] [Save]          (owners only)

Link a repo
Commit this as .panopticon.json at the root of a repo, and its reports go to PAN.
┌──────────────────────────────────────────────┐
│ { "project": "6f1c2e4a-…" }                  │
└──────────────────────────────────────────────┘
[Copy]  Copied.
Or ask your agent to link this repo to PAN: it can look the Project up and write the file.   (small, muted)

Members
──────────────────────────────────────────────
Emmz (you)   emmz@example.com           [Owner ▾]
──────────────────────────────────────────────
Sam          sam@example.com            [Viewer ▾] [Remove]
──────────────────────────────────────────────

Invite someone                          (owners only)
Email [              ]  Role [Viewer ▾]  [Invite]

[Leave this Project]
```

- **The code plate** (`.project-code`) is the code in Gabarito 700 with `letter-spacing: 0.08em`, inside a 1px
  `--ink-muted` border with a 3px radius. It's tighter than an agent pill's, so the two never read alike. Codes
  are data, so their capitals aren't an all-caps label. Three sizes, and only the size changes:
    - **The Project page:** 31px, and the loudest thing on the page, like the device code, because it's what
      you'll look for on the cards.
    - **The Projects list:** 16px, in a fixed 6.5rem column so the names line up.
    - **Cards, the change page header and history rows:** 12.8px. The plate leads the line it sits on: the host
      line on a card ("PAN github.com"), the repo line on a change page, and the meta line on a history row. The
      Project's name is its tooltip. A bootstrap reporter's change, in no Project and nobody's personal work,
      has no plate.
- **"Personal"** (`.project-code.personal`) is the same plate with a dashed `--rule` border, in the body font
  at 400, `--ink-muted` and sentence case. The dash is the empty bar's vocabulary: no Project here.
- **Plain colours only.** Plates are never teal, since `--lit` is progress, and never `--spark`, since that's
  the live signal. A Project has no colour of its own.
- **Quiet cards:** on idle and stale cards the code turns `--ink-muted` with the rest of the card's identity, so
  a quiet card's plate doesn't stand out ahead of live ones (principle 3).
- **The list:** rows between hairlines, like Members. The plate, then the name (600, the row's link, with no
  underline until hover or focus), then the role at the right in small `--ink-muted` text, as "Owner",
  "Editor" or "Viewer". Phones wrap the role under the name. With no Projects: "You're not in any Projects
  yet.", then a muted line: create one below, or ask a Project's owner to invite you.
- **Creating a Project:** a "New Project" subheading, then the Name and Code fields on one line, with "Create
  Project" as the primary button. The code input shows capitals as you type
  (`text-transform: uppercase`, with the plate's letter-spacing), and the value is sent in capitals. The rule
  sits under the Code field as small muted help text, linked by `aria-describedby`. Errors use the usual
  alert line under the form ("The code PAN is taken. Choose another."). A created Project opens its page.
- **The Project page:**
    - **The header:** a small muted "All Projects" back link, then the plate, then the name as the h2. Under
      them, one small muted line gives your role ("You're an owner.") and the id.
    - **Renaming:** owners get the rename form under that line (Name, then Save), like your display name on
      Members.
    - **Link a repo:** the file's content in the empty state's `pre` block, then an outlined Copy button with a
      status line ("Copied."). If the clipboard isn't available, the status says to select the text and copy
      it, so the block is the fallback.
    - **Members:** owners get each role as a small `select` in the row, with a visually hidden label ("Role for
      Sam"). A change applies at once and the status line confirms it ("Sam is now an editor."). The exception
      is stepping down on your own row: that asks first, because only another owner can undo it. Everyone else
      sees the role as muted text. Remove is the small outlined button, after a confirmation.
    - **Invite someone** is owners only: Email, Role (Viewer by default) and the primary Invite button, with one
      muted line: if they aren't on this server yet, this lets them sign in too, and no email is sent.
    - **Leave this Project** is an outlined button at the end, after a confirmation, and it goes back to
      `/projects`. The last owner's refusals ("A Project needs an owner. Make someone else an owner first.")
      appear in the alert line by the control that was used.
- **A Project you aren't in:** "There's no such Project." with the back link, the same as one that doesn't
  exist.

## Pages: a change, and the history

A change's page is where you read; the history is where finished changes go. Both stay quiet, in the tools'
voice, with one exception that belongs to the lit-cells idea.

```
Back to the dashboard
github.com emmz/panopticon
team-auth                                   (Gabarito 700, 31px: the loudest text)
Last report 2m ago
Section 6: CLI bridge   ▮▮▮▮▯        4/5 (80%)
Whole change            ▮▮▮▮▮▮▮▮▯▯▯ 22/30 (73%)
claude-code@vm-1 Emmz
──────────────────────────────────────────────────────────────
Change            ┃ # Design
┃ Proposal        ┃ Rendered Markdown, about 70 characters a line …
  Design          ┃
  Tasks           ┃ ■ 1.1 Done          (a lit cell)
Specs             ┃ □ 1.2 Not yet       (an unlit cell)
  cli-bridge      ┃
Devlog            ┃
  section 1 done · 2026-11-08 18:43
```

- **Header:** a small muted back link (to the dashboard, or to the history when archived), the repo, then
  the change name in Gabarito 700 at 31px. Below it, "Last report 2m ago" or "Archived 8 Nov 2026", the
  card's bars (just the whole-change bar once archived), and the agent pills.
- **Two columns from 56rem:** a 15rem document list, sticky, beside the article. Below 56rem the list
  stacks above it, with the usual 16px gutters.
- **The document list:** sentence-case group labels (Change, Specs, Devlog, Other files) in small 600
  `--ink-muted`, never caps. Plain `--ink` links with no underline. The current document has a 3px `--ink`
  rule at its left and 600 weight (not teal: teal is progress). Devlog titles are a step smaller.
- **The article:** at most 44rem wide (about 70 characters), line-height 1.6. Headings in Gabarito, tables
  with hairlines that scroll sideways on a phone, and code on `--paper` in the system monospace.
- **Task lists are lit cells.** In a rendered document, a ticked task's checkbox is a solid `--lit` cell
  and an unticked one an unlit cell (`--rule`, with an `--ink-muted` inset outline, stronger than a bar's
  so a lone cell still reads). That's the one bold thing on the page, and it's the bars' own vocabulary.
- **Archived while open:** a status line with a 3px `--ink-muted` rule says so, and the documents stay.
- **History:** rows between hairlines, like Members. The change name (Gabarito 700, 20px) is the link,
  then the repo and "Archived 8 Nov 2026" in small muted text, then the agent pills. "Show more" is an
  outlined button. Empty: "No changes have been archived yet", and a muted line on where they come from.
- **Cards:** the change name is the card's link. It keeps its look (no link colour or underline), and the
  focus ring shows it.

## Filters and quiet cards

Quiet cards carry principle 3, and the filter bar is a tool, in the pages' quieter voice.

```
Repo              Owner           Person      Machine       Agent type        Status
[All repos     ▾] [github.com/… ▾] [Everyone ▾] [All machines ▾] [All agent types ▾] [Any status ▾]  Clear filters
┌──────────────────────────┐ ┌──────────────────────────┐ ┆╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┆
│ emmz/panopticon   2m ago │ │ acme/site  Idle, 1h ago  │ ┆ emmz/other  Stale, last  ┆
│ walking-skeleton         │ │ add-auth   (muted)       │ ┆   seen 5h ago            ┆
│ ▮▮▮▮▯         4/5 (80%) │ │ ▮▮▯       2/3 (67%)     │ ┆ ▮▮▯ (grey)  2/3 (67%)    ┆
└──────────────────────────┘ └──────────────────────────┘ ┆╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┆
```

- **The Project filter** comes first in the bar ("Project", with "All Projects"), because it's the widest cut.
  It offers each Project as "PAN: Panopticon", by code, then "Personal" last. The dashboard offers only Projects
  with a card, and the history and metrics offer only those with an archived change. A Project in the address
  that the viewer isn't in shows as "Unknown Project (no changes)", so it can still be cleared.
- **Active:** the card as it always was.
- **Idle:** it loses contrast. The change name and owner/name drop to `--ink-muted`, and the time reads "Idle, 1h
  ago" with "Idle" in `--ink` 600. The lit cells stay teal: progress is still worth reading.
- **Stale:** it recedes into the page.
    - **The surface:** the card takes the `--paper` background and a dashed border (a shape, not only a colour). Its
      text and agent pills are `--ink-muted`.
    - **Cells and time:** the lit cells turn `--ink-muted`, so a stale card holds no teal. The time reads "Stale, last
      seen 5h ago".
- **No opacity:** this replaces the earlier 60% opacity, which took text below 4.5:1. Everything stays at
  `--ink-muted` or stronger.
- **The label:** the state and the time are joined by a comma, not a middot (principle 5), and read aloud that way.
- **Filter bar:** one row above the grid (and above the history list), wrapping as needed.
    - **Selects:** each has its label above it (small, 600), as on the forms. The first option is "All repos",
      "Everyone", "Any status" and so on.
    - **A set filter:** it shows a 2px `--ink` border and 600 text, so the active filters read by shape and weight,
      not colour. Teal stays progress.
    - **Clear filters:** a link button at the row's end, shown only while a filter is set.
    - **A dropped filter:** when the address held one that didn't parse, a muted line says which.
- **On a phone:** the selects pair up, two to a row. There's no "Filters" toggle: what's chosen stays in sight, as it
  does in the address.
- **The count:** "3 of 7 changes" when filters hide some, with "4 hidden by the filters" as its title.
- **Nothing matches:** "No changes match these filters" (Gabarito 700, 25px, like the empty state), a muted line,
  and a Clear filters link button. On the history it's one line, "No archived changes match these filters.", and the
  getting-started page is only for a dashboard with no changes and no filters.

## Metrics

How long changes and their sections take: a panel on the change page and the metrics page. They speak in the pages'
quiet voice. The one bold thing is the **duration rule**: a slow section should stand out after the fact, the way a
quiet card stands out while it's happening.

```
Took 5d 6h                                       (Gabarito 700, 20px)
Wall-clock time, first report to archive.        (small, muted)
 1  Server         ━━━━━━━━━━━                    2h
 2  Web            ━━━━━━━━━━━━━━━━━━━━━━     3h 30m
 3  Docs           ┅┅┅┅┅         in progress, 1h so far
```

- **Duration rule:** a 3px `--ink-muted` line, as long as its time against the longest time beside it. It's at least
  a sliver, so a short time still shows. It's dashed while the section is still running, so state is a shape and not
  only a colour.
    - **Colour:** time isn't progress, so rules are never teal.
    - **Accessibility:** a rule is decorative (`aria-hidden`), and the time is always printed beside it.
- **Durations:**
    - **Display:** short, in Atkinson 600 with tabular figures: "45m", "3h 30m", "5d 6h". They never read "0m":
      under a minute is "<1m".
    - **Long form:** "5 days 6 hours", used as the tooltip and as the screen-reader text (Emmz, 2026-11-09).
    - **In progress:** a running section reads "in progress, 1h so far". The state and the time are joined by a comma,
      not a middot (principle 5). "in progress" is in `--ink` 600 and the rest is muted.
    - **Unfinished:** on an archived change, a section that never finished reads "unfinished, 3h", styled the same,
      with its rule still dashed. Its clock stopped at the archive, so it isn't "so far" (Emmz, 2026-11-09).
- **The change page's panel:**
    - **Placement:** it sits between the header and the documents, 38rem wide, and ends with a hairline.
    - **The headline:** "Took 5d 6h" for an archived change, or "Running for 2d" for an active one, then the
      wall-clock note.
    - **Section rows:** a grid of number (muted, right-aligned), title, rule, then time. On a phone, the rule drops
      under its title.
    - **Empty:** with nothing measured yet, it reads "No section has been started yet." It updates with the page's
      live refresh.
- **The metrics page:**
    - **Width:** one column, 44rem wide.
    - **The filters and the empty state:** the history's filter bar. When nothing matches, the history's no-match
      block with Clear filters.
    - **The figures:** three, in a `dl` row between hairlines: changes archived, median time to archive, and median
      time per section. Each value is Gabarito 700 at 25px over a small muted label. There are no tiles, no shadows
      and no big-number hero. On a phone they sit two to a row.
- **The weekly chart:** hand-built inline SVG.
    - **The marks:** the bars are `--lit` with a 2px radius, the cells' own shape, on a `--rule` baseline. The labels
      ("2 Nov", the week's Monday in UTC) and the top-of-scale value are `--ink-muted`.
    - **Width and phones:** it is 40rem at most, and scrolls sideways on a phone rather than shrink its labels below
      reading size.
    - **Accessibility:** it's `role="img"`, named by a one-sentence summary, with a visually hidden table of every
      week's value.
- **Slowest sections:** rows between hairlines, like the history. Each row reads, in order:
    - the change name (Gabarito 700) as the link, then its owner/name;
    - "Section 2: Core", muted;
    - its time at the right;
    - a full-width rule against the slowest.

## Tags

A tag says what a change _is_ (`bug`, `area: onboarding`). It isn't progress, liveness or a person, so it borrows
none of their colours or shapes. It speaks in the tools' quiet voice. The one deliberate choice is a **border that
says who can see it**:

- **A solid border** is a Project's tag, which every member sees.
- **A dashed border** is your personal tag, which only you see.

The dash is the vocabulary the "Personal" plate and the empty bar already use for "no Project here".

### The chip

```
 PAN/bug   PAN/area: onboarding   ╎dev╎   ╎language: csharp╎
 └ solid: the Project's ┘          └ dashed: only yours ┘
```

- **Shape:** body font (Atkinson 400) at 12.8px, in a 1px `--ink-muted` border with a 3px radius, padded 0 6px.
    - **Not a code plate:** the plate is Gabarito 700 capitals with open letter-spacing.
    - **Not an agent pill:** pills are fully round with a `--rule` hairline.
    - So chips, plates and pills never read alike, even side by side.
- **Two tones in one chip:** the part that tells tags apart is `--ink`. What comes before it is `--ink-muted`:
    - in `PAN/bug`, "PAN/" is muted and "bug" is ink;
    - in `PAN/area: onboarding`, "PAN/area:" is muted and "onboarding" is ink;
    - in `dev`, it's all ink.

    So a row of `area:` tags scans by its values.

- **The code:** a Project tag always carries its code, as the spec words it (`PAN/bug`), even on a card whose
  host line already has the `PAN` plate. A chip can be read on its own: in a filter, a picker or a screen reader.
- **Personal chips:** the same chip with a dashed border. Each has a visually hidden suffix, "(only you see this)",
  and the same words as its title.
- **The row:** a `ul.tag-chips` with `aria-label="Tags"`.
    - **Order:** the project tags first, by key then value, then the personal ones, as the API orders them.
    - **Wrapping:** chips wrap with a 4px gap.
    - **No tags:** a change with no tags renders no row and no gap. It looks as it did before tags.
- **Never teal, never `--spark`:** a tag is neither progress nor live.
- **Quiet cards:** on idle and stale cards, the chips' ink parts drop to `--ink-muted` with the rest of the card's
  identity (principle 3). The borders stay, so solid and dashed still read.

### Where chips sit

```
┌──────────────────────────┐
│ PAN github.com           │
│ emmz/panopticon   2m ago │
│ walking-skeleton         │
│ PAN/bug ╎dev╎            │   ← under the change name: what it is, before how far along
│                          │
│ Section 6: CLI bridge    │
│ ▮▮▮▮▯         4/5 (80%) │
│ …                        │
```

- **On a card:** directly under the change name, 8px above the bars. That's identity (what it is) before progress
  (how far). A card shows at most eight chips, then "+3 more" in small muted text, whose title lists the rest. The
  change page shows them all.
- **On a history row:** after the meta line (plate, repo, "Archived 8 Nov 2026"), before the agent pills.
- **On the change page:** under the change name in the header, with the editor's "Edit tags" button (below) at the
  row's end.

### The change page's picker (owners and editors)

```
walking-skeleton
PAN/bug  PAN/area: onboarding  ╎dev╎   [Edit tags]
```

```
walking-skeleton
PAN/bug  PAN/area: onboarding  ╎dev╎   [Done]
┌ PAN's tags ──────────────────────────────────┐
│ Find or add a tag [area: bi            ]     │
│ ──────────────────────────────────────────── │
│ ✓ area: onboarding                           │
│   area: billing-v1                           │
│ ──────────────────────────────────────────── │
│ + Add PAN/area: bi                           │
└──────────────────────────────────────────────┘
Added PAN/bug.                                    (status line)
Your personal tags come from `panopticon tags` on your machines.
```

- **Opening:** "Edit tags" is a small outlined button, like Remove. It toggles an inline panel under the chips. It's
  not a modal: the change stays in view, and each toggle shows in the chips above at once. The button reads "Done"
  while the panel is open. Escape also closes it, and focus returns to the button.
- **The panel:** 28rem wide at most (full width on phones), with a 1px `--rule` border, a 4px radius, `--card`
  surface and no shadow. Its heading, "PAN's tags", is small 600 `--ink-muted`, like the document list's group
  labels.
- **Search:** "Find or add a tag" is a labelled input, focused when the panel opens. It narrows the list by key and
  value, ignoring case.
- **The list:** one row per Project tag, by key then value, between hairlines. Each row is a real checkbox: checked
  means the change has the tag.
    - **The mark:** an `--ink` check mark (✓) in a `--rule` square, never a lit cell. Teal is progress, and a lit cell
      on this page means a done task.
    - **Inside the panel:** the code is left off ("area: onboarding"), because the heading already says whose tags
      these are.
    - **Toggling:** a toggle applies at once. The status line (`aria-live="polite"`) confirms it: "Added PAN/bug." or
      "Removed PAN/bug.". The action keeps its name in the confirmation.
    - **Long lists:** past ten rows, the list scrolls inside the panel (max-height 20rem), and the add row stays in
      sight.
- **Adding a new tag:** when the typed text parses and equals no tag in the list, a last row reads "+ Add
  PAN/area: billing", with the code, because it creates a Project tag that everyone sees.
    - **What it does:** it adds the tag to the Project and puts it on the change, in two calls. The status reads "Added
      PAN/area: billing to PAN's tags and to this change."
    - **If the second call fails:** the tag stays in the list, unchecked, and the error says so.
- **Typed text that isn't a tag:** no add row. The panel's alert line gives the parse error as the API words it
  (`"has space": a key can only have letters, digits, '-', '_' and '.'`).
- **Personal tags:** they aren't edited here, because they come from the machines. One muted line under the panel
  says where they do come from.
- **Errors:** the usual alert line inside the panel, with a 3px `--spark` rule. For example "PAN has 200 tags, the
  most it can have. Delete one first." or "Only the Project's owners and editors can tag its changes." (the role
  changed while the page was open).

**The states:**

| State                       | What shows                                                                      |
| --------------------------- | ------------------------------------------------------------------------------- |
| Viewer                      | The chips only, with no button.                                                 |
| Editor, change has no tags  | No chip row. "Edit tags" sits alone under the change name.                      |
| Editor, Project has no tags | The panel opens to "PAN has no tags yet. Type one to add it." above the search. |
| Personal change             | Personal chips only, and no button. A personal change has no project tags.      |
| Archived change             | The same as active: tags can still be edited.                                   |
| Error                       | The alert line in the panel. The checkbox goes back to what the server has.     |

### The Project page's Tags section

It sits between "Link a repo" and "Members", because it's about the Project's work, not its people.

```
Tags
──────────────────────────────────────────────
PAN/area: onboarding      3 changes   [Rename] [Delete]
──────────────────────────────────────────────
PAN/bug                  12 changes   [Rename] [Delete]
──────────────────────────────────────────────
Tag [area: billing   ] [Add tag]                 (owners and editors)
    A key, or a key and a value: bug, or area: onboarding.
```

Renaming, in place:

```
──────────────────────────────────────────────
Rename PAN/bug [defect          ] [Save] [Cancel]
──────────────────────────────────────────────
```

- **The rows:** rows between hairlines, like Members. The chip comes first, as on cards, so you learn what it looks
  like. Then the count in small `--ink-muted` text: "No changes", "1 change" or "12 changes". On a phone, the
  buttons wrap under the chip.
- **Rename and Delete:** small outlined buttons at the row's right, for owners and editors.
    - **Rename** turns the row into a labelled input holding the current text, with Save (primary) and Cancel. Enter
      saves and Escape cancels. A refusal shows in the alert line under the input, for example "PAN already has
      PAN/area: onboarding.", and the input keeps what was typed.
    - **Delete** asks first, through the pages' usual confirm, with the count: "PAN/bug is on 12 changes. Delete it?"
      With no changes, it's "Delete PAN/bug? No changes carry it." The status line then reads "Deleted PAN/bug."
- **Adding:** a Tag input with "Add tag" as the primary button, and the syntax as muted help text linked by
  `aria-describedby`. An added tag joins the list in its sorted place with "No changes", and the input clears.
    - **Errors** go in the alert line under the form: a parse error, a duplicate, or the limit.
    - **At the limit:** with 200 tags, the form stays, and the help line says "PAN has 200 tags, the most it can
      have."
- **Live:** the list follows `project-tags-changed`, so a rename by someone else shows without a reload.
    - **Only the rows move.** Typed text in the add form and the invite form stays, and so does focus.
    - **A rename in progress stays open** if someone else edits another tag, and its Cancel shows the tag's name as it
      is now.
    - **If the tag being renamed is deleted,** the form closes, the row goes, and the status line reads "PAN/bug was
      deleted."
    - **Counts** update with the list, not each time a change is tagged.

**The states:**

| State                  | What shows                                                                             |
| ---------------------- | -------------------------------------------------------------------------------------- |
| Viewer                 | The rows and counts only, then one muted line: "Owners and editors manage these tags." |
| Owner or editor        | The rows with Rename and Delete, then the add form.                                    |
| Empty, viewer          | "PAN has no tags yet."                                                                 |
| Empty, owner or editor | "PAN has no tags yet. Add one here, or from a change's page." Then the form.           |
| Error                  | The alert line by the control that was used, as elsewhere on the page.                 |

### The Tags filter

One multi-select control in the filter bar, after Status, on the dashboard, the history and the metrics page.

```
Project          Repo         …  Status         Tags
[All Projects ▾] [All repos ▾] … [Any status ▾] [2 tags ▾]  Clear filters
Showing changes with all of:  PAN/bug ×   ╎dev╎ ×
```

Open:

```
[2 tags ▴]
┌──────────────────────────────────────┐
│ Find a tag [lang            ]        │
│ Yours                                │
│ ☐ language        any value          │
│ ☐   language: csharp                 │
│ PAN                                  │
│ ☑ bug                                │
│ ☐ language        any value          │
│ ☐   language: rust                   │
└──────────────────────────────────────┘
```

- **The control:** a button that looks like the bar's selects, with "Tags" above it.
    - **What it reads:** "Any tags" when none is chosen. With some chosen, it says how many ("2 tags") and takes the
      set filter's style (a 2px `--ink` border and 600 text).
    - **Opening:** it opens a popover under it, 22rem wide (full width on phones), with the picker's surface.
    - **Its parts:** a "Find a tag" search, focused on open, then a list of checkboxes in groups.
    - **Closing:** Escape or a click outside closes it, and focus returns to the button.
- **The groups:** "Yours" first (the personal tags, without the dash, since the group label says whose), then one
  group per Project by code, the group labelled with the code alone. That's the API's order.
    - **Inside a group:** a key that has values gets a key-only entry first, "language" with "any value" muted at the
      right. Its values follow, indented one step.
    - **No group for the empty:** a group with no choices doesn't render.
    - **Nothing to offer:** the popover reads "None of these changes have tags."
- **Choosing:** each toggle applies at once and updates the address, like the selects. Choosing a value under a
  chosen key-only entry is allowed: both apply, and the list says nothing about it.
- **The chosen tags:** they show as chips on a line under the bar, after "Showing changes with all of:" in small
  muted text. That phrase is the AND, said once.
    - **Each chip:** the usual chip (solid or dashed), with a × button labelled "Remove PAN/bug from the filter".
    - **A key-only chip:** it reads "PAN/language: any".
    - **Clear filters** clears the tags too.
- **A tag the address names but the choices don't** (a deleted tag, or a key no change has any more): its chip
  reads "Unknown tag", with ×. Nothing matches, so the usual "No changes match these filters" block offers Clear
  filters. A renamed tag shows its new name, because it goes by id.
- **Dropped values:** a malformed `tag` value, or any past 20, joins the bar's muted "dropped" line ("Dropped a tag
  filter that wasn't valid.", or "Kept the first 20 tags.").
- **On a phone:** the Tags control takes a whole row, and the chosen chips wrap under it.

**The states:**

| State                  | What shows                                                                                               |
| ---------------------- | -------------------------------------------------------------------------------------------------------- |
| Nothing chosen         | "Any tags", and no chip line.                                                                            |
| Some chosen            | "2 tags" in the set style, and the chip line.                                                            |
| Unknown in the address | An "Unknown tag" chip with ×, and no-match if nothing else matches.                                      |
| No tags anywhere       | The control is still there, and opens to "None of these changes have tags."                              |
| Error                  | Like the other filters: the choices come with the page's data, so a failed load is the page's own error. |

### Accessibility

- **Chips:** they're text, so there's no contrast trick: the ink and muted parts are each ≥ 4.5:1 on the card. The
  dashed border isn't the only cue, because every personal chip carries its hidden "(only you see this)".
- **The picker:** a `fieldset` of real checkboxes, with the legend "PAN's tags". The add row is a button. The status
  line is `aria-live="polite"`, and errors are `role="alert"`.
- **The filter:** the button has `aria-expanded` and `aria-controls`. The popover is a group of checkboxes per
  group (`role="group"`, labelled by the group name), not a custom listbox, so the keyboard works as everywhere
  else: Tab, Space, Escape. The × buttons have their full labels.
- **Focus:** the usual 2px `--ink` outline everywhere, and nothing is reachable only on hover.

## Sessions and prompts

Agent interaction (agent-interaction D10) adds who is working on a change right now, and the prompts that wait for
someone. Lit cells stay the memorable thing. The one new loud mark is **"Needs you"**, because it's the only thing
on the dashboard that blocks an agent until a person acts.

```
│ claude-code@vm-1 Emmz                   │
│ ─────────────────────────────────────── │
│ ❯ Run make gates 4m                     │
│ ❯ waiting on a question 2m              │
│ ┃ Needs you                             │
│ ┃ Store Which store?                    │
│ ┃ ( ) Postgres  the one we run          │
│ ┃ ( ) Redis                             │
│ ┃ Other [                    ]          │
│ ┃ [Send answer]                         │
└─────────────────────────────────────────┘
```

- **Session rows** sit under the agents, below a hairline `--rule`. They're text only. What the session is doing
  is in `--ink` at body size (16px), after a `❯` in `--prompt` (the terminal mark; decoration, hidden from screen readers), then its time, small, in `--ink-muted`, with tabular figures. Who and where
  ("Emmz on mbp") and the full timestamp are in the time's tooltip, and in hidden text for screen readers, which
  read the whole row with commas (principle 5). (Emmz, 2026-11-11.) There's no state dot: it would compete with
  the cells.
- **"Needs you"** (the viewer may answer) is a well: `--paper` fill, a 3px `--spark` rule on the left, and the
  marker in 600. It holds the prompt's form while it's held, or the question and "answer in the terminal" while
  it isn't. The card also takes a `--spark` hairline border (`.card--needs-you`), so it can be found across the
  grid. That's the same attention mark as an error, or a just-updated card's edge.
- **"Needs Emmz: question"** (someone else's prompt) is one muted line with no well. There's nothing there for the
  viewer to do.
- **The prompt form** uses the pages' form vocabulary inside the card:
    - The question header is 600, then the question in ink.
    - Options are native radios or checkboxes with `accent-color: var(--ink)`, never teal. Each label is 600, with
      its description muted under it.
    - A preview is a hairline `--card` block, indented under its option.
    - Send answer and Allow once are solid `--ink`. Deny and the Yes and No toggles are outlined, and a pressed
      toggle turns solid ink.
    - A permission prompt's input is the one monospace block: it's a command.
    - Errors use the page error mark: 600, with a 3px `--spark` rule.
- **Header toggles:** Away and Notifications share one form. Each is a hairline pill reading "Away off" or "Away on"
  (the state is in the text, and `aria-pressed` says it too), and it turns solid `--ink` when on. They're not
  spark (spark means live) and not teal (teal means progress). Notifications is hidden where the browser has none.
- **Answers on the change page:** "Answers from the dashboard" (Gabarito 20px), then rows between hairline dividers.
  Each reads "Bob answered a question for Emmz, 5m ago", with the questions under it: the question muted,
  the answer 600. On phones, each question stacks above its answer.

## Accessibility floor

- Each bar is `role="progressbar"` with `aria-label` (e.g. "Section 6 progress") and `aria-valuenow`,
  `aria-valuemin` and `aria-valuemax` (done / 0 / total). Its `aria-valuetext` is "4 of 5 tasks, 80
  percent".
- Text contrast is at least 4.5:1, and cells and borders are at least 3:1.
- Keyboard focus is visible: a 2px `--ink` outline, offset by 2px. That matters once cards become links
  (drill-down, in a later change).
