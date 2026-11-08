# 7.1 approved: "lit cells", with the name "a PANOPTICON"

<!-- Section: 7 · Task: 7.1 done · Base: 1b7cd82 · uncommitted until §7 ends -->

Emmz approved the direction in `web/DESIGN.md` ("looks great"), with one change and one answer.

- **The site title is "a PANOPTICON".** It's a play on "the Panopticon", and the "a" is for *agent*.
  - Use that exact casing in the wordmark and the page `<title>`, everywhere the product is named in the UI.
  - Recorded in DESIGN.md's new "Name" section. It's the one exception to the no-all-caps rule.
  - Wordmark letter-spacing is now `0.03em`.
  - Code identifiers, the CLI name (`panopticon`) and the docs are unchanged.
- **Fonts: self-host via Fontsource** (`@fontsource/gabarito`, `@fontsource/atkinson-hyperlegible-next`).
  - These are npm packages in `web/`, not machine dependencies, so `docs/dependencies.md` is unchanged.
  - npm installs in the sandbox need `--cache "<scratchpad>/npm-cache"` and `registry.npmjs.org` allowed.
- The preview (scratchpad `design-preview.html`) was updated with the new wordmark. It's throwaway.

Still uncommitted: `web/DESIGN.md` and `.gitignore` (`.mcp.json`). Both go into the §7 commit.

## Next

- **7.2:** the card list from `GET /api/changes`.
  - Card content, the two labelled lit-cell bars with counts, the empty-bar state, and the empty dashboard
    state.
  - Vitest tests for "One card per change", "Card content" and "Two progress bars".
  - Add the Fontsource packages, and set the `index.html` title to "a PANOPTICON".
