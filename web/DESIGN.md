# Dashboard design: "lit cells"

The brief (docs/DISCOVERY.md) is **fun but clean**, a glance that replaces SSH, and stalls obvious at once.

A panopticon is a round tower from which one watcher sees every cell. On the dashboard, each change is a
block of cells and each task in `tasks.md` is one cell. A finished task is a **lit cell**. That's the one
bold idea, and everything around it stays quiet.

## Name

The site title and wordmark are **"a PANOPTICON"**: a play on "the Panopticon", where the "a" is for
_agent_. Keep the lower-case "a" and the capitals exactly, in the wordmark, the page `<title>` and anywhere
else the product is named in the UI. The wordmark is set in Gabarito 700 with `letter-spacing: 0.03em` to
open up the capitals. The "a" gets the same colour and weight: the joke is in the words, not in the
styling. (This is a deliberate exception to "no all-caps", because it's the name.)

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
- Stale cards drop to 60% opacity, and their lit cells become `--ink-muted`.

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
a PANOPTICON              ● Live  3 changes   Emmz  Members  Tokens  Sign out
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
  Members, Tokens and Sign out as small muted links with a `--rule` underline. On phones the header wraps,
  with the account on its own line.
- **Empty dashboard:** "No agents have reported yet." Below it, one line: connect one with the panopticon
  CLI, then ask it to report progress on a change. Then the two commands in a copyable block:
  `panopticon login --server <this origin>` and `claude mcp add panopticon --scope local -- panopticon mcp`.

## Pages: sign-in, members, tokens and device approval

These are tools, not views of progress, so they stay quieter than the cards and never use `--lit`.

```
Members
──────────────────────────────────────────────
Emmz (you)      emmz@example.com   Joined 8 Nov 2026
  Your display name [Emmz            ] [Save]
──────────────────────────────────────────────
Sam             sam@example.com    Joined 9 Nov 2026     [Remove]
──────────────────────────────────────────────

Invite someone
They become a member at once, and sign in with their email.
Email [               ]  Display name (optional) [        ]  [Invite]
```

- **One left-aligned column**, 38rem wide, under the header. The page heading is Gabarito 700 at 25px, the
  same as the empty state's. Subheadings ("Invite someone") are Gabarito at 20px. Lead text is
  `--ink-muted`.
- **Lists, not cards:** members and tokens are rows between hairline `--rule` dividers. The name (or the
  machine) is 600, and the email, dates and last use follow in small muted text. Cards stay reserved for
  changes.
- **Forms:** a label above each input (small, 600). Inputs and their button sit on one line and wrap on
  phones. Inputs have a 4px radius and a border dark enough to meet 3:1.
- **Buttons:** the main action of a page (Send me a link, Sign in, Invite, Continue, Approve) is solid
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
  "Sign in to a PANOPTICON?" (the server can't say whose link it is before the confirm). One "Sign in"
  button. A used or expired link explains which, with a "Send me a new link" link.

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

## Accessibility floor

- Each bar is `role="progressbar"` with `aria-label` (e.g. "Section 6 progress") and `aria-valuenow`,
  `aria-valuemin` and `aria-valuemax` (done / 0 / total). Its `aria-valuetext` is "4 of 5 tasks, 80
  percent".
- Text contrast is at least 4.5:1, and cells and borders are at least 3:1.
- Keyboard focus is visible: a 2px `--ink` outline, offset by 2px. That matters once cards become links
  (drill-down, in a later change).
