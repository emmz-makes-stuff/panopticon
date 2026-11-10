# Dashboard design: "lit cells"

The brief (docs/DISCOVERY.md) is **fun but clean**, a glance that replaces SSH, and stalls obvious at once.

A panopticon is a round tower from which one watcher sees every cell. On the dashboard, each change is a
block of cells and each task in `tasks.md` is one cell. A finished task is a **lit cell**. That's the one
bold idea, and everything around it stays quiet.

## Name

The site title and wordmark are **"PANOPTICON"**, in capitals, with no article. Use it exactly so in the
wordmark, the page `<title>` and anywhere else the product is named in the UI. The wordmark is set in
Gabarito 700 with `letter-spacing: 0.03em` to open up the capitals. (This is a deliberate exception to "no
all-caps", because it's the name.)

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
PANOPTICON                ● Live  3 changes   Emmz  Members  Tokens  Sign out
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
- **Header:** the wordmark, which is also the link home and keeps its name styling (no underline, no link
  colour). On the dashboard, the live state follows, pushed right: a `--spark` dot and "Live", or a
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

## Projects

A Project's code is the number on its cell's door. The tower has many cells, and the plate says whose is whose.
That gives the Projects pages their one deliberate thing, the **code plate**. Everything else is in the tools'
quiet voice, as above.

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

## Accessibility floor

- Each bar is `role="progressbar"` with `aria-label` (e.g. "Section 6 progress") and `aria-valuenow`,
  `aria-valuemin` and `aria-valuemax` (done / 0 / total). Its `aria-valuetext` is "4 of 5 tasks, 80
  percent".
- Text contrast is at least 4.5:1, and cells and borders are at least 3:1.
- Keyboard focus is visible: a 2px `--ink` outline, offset by 2px. That matters once cards become links
  (drill-down, in a later change).
