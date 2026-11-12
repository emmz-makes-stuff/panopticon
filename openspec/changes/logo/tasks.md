# Tasks

Uses `docs/images/logo.webp` as the logo. The PNGs are made once and committed, with no new dependency (design D1).
Section 3 ends with a browser check by Emmz, which is a stop (CLAUDE.md "Stop and ask").

## 1. Assets

- [x] 1.1 Measure the source with `dwebp -ppm` and a scratchpad Node script. Find the rounded square's inset and
  corner radius, from the white-to-dark transition along the top edge, the left edge and the diagonal, and the
  background colour (design D1). Verify: the inset, radius and colour, and the commands that found them, are recorded
  in a devlog note.
- [x] 1.2 Generate `web/public/favicon-32.png`, `web/public/apple-touch-icon.png`, `web/public/logo.png`,
  `docs/images/logo.png` and `docs/images/social-preview.png` at the sizes and corner treatments in design D2, using
  the scratchpad script (mask, area-average downsample, Node `zlib` PNG writer). Verify: `sips -g pixelWidth -g
  pixelHeight -g hasAlpha` on each file matches D2. The four transparent-corner files have alpha 0 at pixel (0,0) and
  are opaque at the centre. The touch icon and social preview have no transparent pixels. The social preview is under
  1 MB. Each file, viewed with Read, shows no white rim or fringe.
- [x] 1.3 Record how the assets were made in the devlog note: the full script and the commands, so they can be made
  again from the WebP. Verify: the note holds the script text and the exact commands.

## 2. Dashboard header and site icon

- [ ] 2.1 Add the two icon links to `web/index.html` (design D5). Add a Vitest test that reads `web/index.html` and
  checks `rel="icon"` (`type="image/png"`, `sizes="32x32"`, `href="/favicon-32.png"`) and
  `rel="apple-touch-icon"` (`href="/apple-touch-icon.png"`), and that both files exist in `web/public/`. Verify: the
  test passes, and fails if either link is removed. This covers the spec's "Browser tab" scenario.
- [ ] 2.2 Add the shared logo helper beside `PRODUCT_NAME` in `web/src/app.ts`, and use it in the shell's `a.wordmark`
  and in `app.ts`'s standalone `h1.wordmark` (design D3). Add tests in `web/src/header.test.ts` and
  `web/src/app.test.ts`:
  - The shell header's home link contains `img.wordmark-logo` as its first child, with `alt=""` and
    `src="/logo.png"`, then the text `PANOPTICON`.
  - The link's text content is exactly `PANOPTICON`.
  - The sign-in route's header has the logo too.
  - The standalone dashboard header has the logo before the wordmark.

  Verify: `make web-test` → `WEB_TEST_EXIT:0`. This covers the "Signed-in page", "Sign-in page" and "Screen reader"
  scenarios.
- [ ] 2.3 Style `.wordmark-logo` and the wordmark's flex row in `web/src/styles.css` (design D4). Verify:
  `make web-build`, `make web-format` and `make web-lint` exit 0.
- [ ] 2.4 Add a server test in `tests/Panopticon.Server.Tests/Dashboard/DashboardEndpointTests.cs`, following the
  existing temporary-web-root pattern. It puts `favicon-32.png` and `apple-touch-icon.png` in the web root, and
  checks that a client with no cookie gets 200 `image/png` for each. Verify: `make test` → `TEST_EXIT:0`. This covers
  "Icons without a session".
- [ ] 2.5 Update `web/DESIGN.md`:
  - **Name:** the logo goes with the wordmark.
  - **Header:** the logo comes first, inside the home link, and is decorative.
  - **Layout sketch:** show the logo.
  - **New Logo section:** the D2 asset table, the source file, and transparent corners everywhere except the touch
    icon and social preview.

  Verify: `make web-format` exits 0, and the text matches D2 to D4.

## 3. README, social preview and browser check

- [ ] 3.1 Put the logo above `# PANOPTICON` in `README.md` (design D6). Verify: the scratchpad link checker finds
  `docs/images/logo.png`, with 0 broken links.
- [ ] 3.2 Run `make gates` → `GATES_EXIT:0`.
- [ ] 3.3 Browser check by Emmz (human verification, a stop). Write a `stopped-browser-check` devlog note with a
  copy-pasteable recipe: run the server and the dashboard locally, then check four things:
  - the tab icon;
  - the header logo on the dashboard, Members and sign-in pages, in light and dark schemes;
  - phone width (the header wraps, and the logo stays with the wordmark);
  - clicking the logo goes home.

  Verify: Emmz confirms, or asks for a size or spacing tweak, which is made and rechecked.

## Workflow follow-up

- Emmz uploads `docs/images/social-preview.png` under the repository's Settings → General → Social preview.
- After review, `/opsx:archive` creates the `brand-identity` main spec.
