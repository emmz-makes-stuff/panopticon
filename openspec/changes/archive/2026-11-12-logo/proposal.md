# Proposal

## Why

Panopticon now has a logo (`docs/images/logo.webp`, an eye in a triangle on a dark rounded square), but nothing
uses it. The dashboard has no favicon, so the browser tab shows a blank page icon. The header and the README show
only the word "PANOPTICON". The repo is public now, so a recognisable mark helps both on a tab and on a shared
link.

## What Changes

- **Dashboard header:** every page shows the logo just before the `PANOPTICON` wordmark, sized to the wordmark. It
  belongs to the same link home.
- **Site icon:** the page declares a PNG favicon for the browser tab and an `apple-touch-icon` for home screens.
  Both are served from the built UI.
- **README:** the logo sits above the `PANOPTICON` heading at the top of the front page.
- **GitHub social preview:** a 1280×640 PNG for Emmz to upload under Settings → General → Social preview. This is a
  repository setting, not code.
- **Assets:** PNGs made from the WebP source, with transparent corners in place of the source's white ones.
- **`web/DESIGN.md`:** the Name and Header sections describe the logo, and a short Logo section lists the asset files
  and where each is used.

## Capabilities

### New Capabilities

- `brand-identity`: how the product presents itself in the UI. It covers the logo beside the wordmark in the
  header and the site icon the browser shows.

### Modified Capabilities

None. No existing requirement covers the header wordmark or the site icon. `live-dashboard`'s header requirements
are about the away switch and the tab title, and those don't change.

## Impact

- **Web:**
  - `web/index.html` (icon links);
  - `web/src/shell.ts` and `web/src/app.ts` (header logo);
  - `web/src/styles.css`;
  - new files in `web/public/`, which Vite copies into the server's `wwwroot`;
  - tests in `web/src/`.
- **Docs:** `README.md`, `web/DESIGN.md` and the new images in `docs/images/`.
- **Server and CLI:** no change. `UseStaticFiles` already serves anything in `wwwroot`, and the SPA fallback leaves
  paths with a file extension alone.
- **Dependencies:** none new. The PNGs are made once and committed (see design).
