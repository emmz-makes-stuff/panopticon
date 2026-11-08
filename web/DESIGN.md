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
a PANOPTICON                                     ● Live      3 changes
──────────────────────────────────────────────────────────────────────
┌──────────────────────────┐ ┌──────────────────────────┐ ┌─────────
│ github.com               │ │ dev.azure.com            │ │ (dimmed)
│ emmz/panopticon   2m ago │ │ acme/Nova       14m ago  │ │ …  3h ago
│ walking-skeleton         │ │ add-auth                 │ │
│                          │ │                          │ │
│ Section 6: CLI bridge    │ │ Section 1: Tokens        │ │
│ ▮▮▮▮▯         4/5 (80%) │ │ ▮▯▯           1/3 (33%) │ │
│ Whole change             │ │ Whole change             │ │
│ ▮▮▮▮▮▮▮▮▮▯▯▯ 22/30 (73%)│ │ ▮▯▯▯▯▯▯▯▯     1/9 (11%) │ │
│ claude-code@vm-1         │ │ cursor@laptop            │ │
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
- **Header:** the wordmark, and on the right the live state. That's a `--spark` dot and "Live", or a
  hollow dot and "Offline, reconnecting…" while disconnected. The number of changes sits beside it.
- **Empty dashboard:** "No agents have reported yet." Below it, one line: connect one with the panopticon
  CLI, then ask it to report progress on a change. Then the `claude mcp add` command from `docs/cli.md` in
  a copyable block.

## Accessibility floor

- Each bar is `role="progressbar"` with `aria-label` (e.g. "Section 6 progress") and `aria-valuenow`,
  `aria-valuemin` and `aria-valuemax` (done / 0 / total). Its `aria-valuetext` is "4 of 5 tasks, 80
  percent".
- Text contrast is at least 4.5:1, and cells and borders are at least 3:1.
- Keyboard focus is visible: a 2px `--ink` outline, offset by 2px. That matters once cards become links
  (drill-down, in a later change).
