# Design

## Context

- **The source** is `docs/images/logo.webp`: 1024×1024, with no alpha channel. Its rounded-square background is about
  `#121929`, which is close to the dark theme's `--paper` (`#12152B`). The corners outside the rounded square are
  opaque near-white (`#FEFEFE`), so the image can't be used as it is on either theme, or on GitHub's dark mode.
- **Two places draw the header**, and both use `PRODUCT_NAME`:
  - `web/src/shell.ts` draws it for every routed page, the sign-in page included. There it's
    `h1.wordmark-heading > a.wordmark`.
  - `web/src/app.ts` draws its own `h1.wordmark` only when `mountApp` runs without the shell's `statusSlot`.
- **Serving:** Vite copies `web/public/` into the server's `wwwroot` as it is (`build.outDir`). `UseStaticFiles` runs
  before authentication, and the SPA fallback skips paths with a file extension, so files in `web/public/` are served
  without a session.
- **Tools on hand:** `dwebp` and `cwebp` are installed on this machine but aren't in `docs/dependencies.md`. There's
  also `sips` (macOS) and Node's built-in `zlib`. No image library is in the web or .NET dependencies.

## Goals / Non-Goals

**Goals:**
- The logo sits before the wordmark on every page, with transparent corners on both themes.
- The browser tab and the iOS home-screen icon both show the logo.
- The logo is at the top of the README, and there's a social-preview PNG ready for Emmz to upload.

**Non-Goals:**
- A web app manifest or PWA icons (192/512). Those belong with the planned PWA notifications change.
- A `favicon.ico`. Every current browser takes a PNG `<link rel="icon">`. A missing `/favicon.ico` is a harmless 404,
  because the fallback leaves paths with an extension alone.
- Redrawing the logo as SVG, or changing the artwork.
- Changing the GitHub repository setting. Emmz uploads the social preview herself.

## Decisions

### D1. Make the PNGs once, commit them, and add no dependency

The PNGs are made once from the WebP with a throwaway script in the scratchpad, and the results are committed. The
script:

1. decodes the WebP with `dwebp -ppm`;
2. applies a rounded-rectangle alpha mask with 4×4 supersampled edges;
3. downsamples by area-averaging (premultiplied alpha), so small sizes stay crisp;
4. writes the PNG with Node's `zlib`.

The exact commands go in a devlog note, so the files can be made again later.

- **Measuring the mask:** the corner radius is measured from the source along the top edge and the left edge, where
  the background turns from near-white to dark. The rounded square's inset is checked the same way, since the source
  may have a margin. That keeps the mask from cutting into the artwork or leaving a white rim.
- **Alternatives rejected:**
  - Adding `sharp` or ImageMagick as a dependency, for a job done once.
  - Clipping the corners with CSS `border-radius`. That fixes the header only, not the favicon, touch icon or README.
- **What a later redraw needs:** only `dwebp` and Node, which the devlog note records. It's a maintainer-only one-off,
  so it gets no `make deps` row.

### D2. The asset set

| File | Size | Corners | Use |
| --- | --- | --- | --- |
| `web/public/favicon-32.png` | 32×32 | transparent | `<link rel="icon" type="image/png" sizes="32x32">` |
| `web/public/apple-touch-icon.png` | 180×180 | filled with the logo background | `<link rel="apple-touch-icon">`. iOS rounds the corners itself, and transparent corners would turn black |
| `web/public/logo.png` | 128×128 | transparent | header logo, shown at about 40 CSS px, so sharp up to 3× |
| `docs/images/logo.png` | 512×512 | transparent | README |
| `docs/images/social-preview.png` | 1280×640 | n/a | GitHub social preview: the logo centred at 400 px on a full-bleed logo-background field, under GitHub's 1 MB limit |

`docs/images/logo.webp` stays as the source.

The social preview carries only the logo, with no text. GitHub already shows the repository name beside it, and
drawing the wordmark would mean rendering Gabarito outside a browser.

### D3. The header markup

- **In `shell.ts`:** an `<img class="wordmark-logo" src="/logo.png" alt="" width="128" height="128">` is placed inside
  `a.wordmark`, before the text. The link's accessible name stays "PANOPTICON" because `alt=""` makes the image
  decorative. Clicking the logo goes home because it's inside the link.
- **In `app.ts`:** the standalone header does the same inside its `h1.wordmark`. Both build the image with one shared
  helper exported from `app.ts`, next to `PRODUCT_NAME`, so they can't drift.

### D4. Size and alignment

- `.wordmark-logo` is `height: 1.3em; width: auto`, so about 40 px on the 31 px wordmark. It sits on a flex row:
  `a.wordmark` and `h1.wordmark` get `display: inline-flex; align-items: center; gap: 0.35em`.
- The logo is a dark tile on both themes. On light `--paper` it reads as an app-icon tile. On dark `--paper` the
  tile's edge nearly vanishes, and the glowing triangle carries it. Both are intended.
- These sizes are a first guess, which is why the browser check exists.
- On phones the header already wraps, and the logo and wordmark stay together on one line.

### D5. Icon links in `index.html`

Two `<link>` tags go in `<head>`, with root-absolute hrefs (`/favicon-32.png`, `/apple-touch-icon.png`). Client
routes like `/changes/<id>` then still resolve them. Vite leaves `public/` files unhashed, at those paths.

### D6. README

The README opens with `<p align="center"><img src="docs/images/logo.png" width="128" alt=""></p>`, then the
`# PANOPTICON` heading. The alt text is empty because the heading right after it names the product.

## Risks / Trade-offs

- **Committed binaries can drift from the source.** The source is unlikely to change often, and the devlog note
  records how to make them again.
- **A hand-rolled resampler and mask might look soft or fringed at 32 px.** Each PNG is checked by eye after it's made,
  and Emmz checks the tab and header in a real browser before the change lands.
- **The light-theme tile may feel heavy beside the wordmark.** If it does, the size in D4 is the one knob to turn;
  recolouring the artwork is out of scope.
