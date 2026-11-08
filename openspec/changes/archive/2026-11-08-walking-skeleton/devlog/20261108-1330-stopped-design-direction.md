# Stopped: §7.1 design direction needs Emmz's look-and-feel sign-off

<!-- Section: 7 · Task: 7.1 · Base: 1b7cd82 · Status: stopped, uncommitted on purpose -->

**Why stopped:** CLAUDE.md's "Stop and ask" lists the `frontend-design` result as a human-verification
point. 7.1 is unticked, and the work is uncommitted.

**Uncommitted:**

- `web/DESIGN.md`: the direction.
- `.gitignore`: added `.mcp.json`, per Emmz's answer after §6 ("Keep untracked, gitignore it"). It goes in
  the §7 commit.

**Direction ("lit cells"):**

- Each task is a cell. Bars are segmented, one segment per task, lit (`--lit` teal) when done. Above 40
  tasks the cells merge into a continuous fill.
- Cool paper with indigo ink (avoiding the cream and near-black defaults), and a `--spark` pink only for
  the live dot and a just-updated card's left edge.
- Gabarito 700 for the wordmark and change names, and Atkinson Hyperlegible Next for everything else.
- Cards have a hairline border, a 6px radius and no shadow. Stale cards go to 60% opacity with muted
  cells.
- The one motion: newly lit cells light in sequence, 60 ms apart, and reduced motion makes it instant.
- Copy uses no all-caps, no middots, and counts like "4 of 5, 80%".
- Contrast checked: text ≥ 5.6:1, and lit vs unlit cells and lit vs card ≥ 3.2:1, in both themes.

**Preview (not in the repo):**
`/private/tmp/claude-501/-Users-rendle-github-emmz-panopticon/1f30b2da-b8ef-45d5-9e6a-297e22f09e5d/scratchpad/design-preview.html`
has five cards (just updated, multi-agent, dense at 64 tasks, stale, and no tasks), plus the empty
dashboard. Its buttons replay a report, toggle offline and toggle the theme. It loads the fonts from
Google Fonts. If the scratchpad is gone, rebuild it from `web/DESIGN.md`.

**Questions for Emmz:**

1. Does the direction land as "fun but clean"? Approve it, or say what to change.
2. Fonts: self-host via Fontsource packages in `web/` (new npm dev dependencies), or Google Fonts?

**Server state:** the dev server from the §6.5 check is still running on 7480 (`dotnet run` +
`Panopticon.Server`). Stop it by PID, never by port.

## Next

- **On approval:** apply any tweaks to `web/DESIGN.md`, tick 7.1, and start **7.2** (the card list from
  `GET /api/changes`, plus Vitest tests). Follow the font answer; Fontsource means adding the npm package
  and noting it in the devlog. §7 commits only after 7.4 (one commit per section).
- **On changes requested:** revise `web/DESIGN.md` and the preview, and ask again.
