# Stopped: browser check of the logo (task 3.3)

<!-- Section: 3 · Tasks: 3.1–3.2 done (uncommitted), 3.3 waiting · Base: 9d21c14 -->

This is a stop for human verification (CLAUDE.md "Stop and ask": dashboard look and feel).

**Done but not committed:** 3.1 (the README logo above `# PANOPTICON`) and 3.2. The link checker found 0 broken
links in 18 files, and `docs/images/logo.png` exists. The checker reads only Markdown links, not the HTML `src`.

```
BUILD_EXIT:0
AOT_EXIT:0
TEST_EXIT:0
FORMAT_EXIT:0
WEB_INSTALL_EXIT:0
WEB_BUILD_EXIT:0
WEB_TEST_EXIT:0
WEB_FORMAT_EXIT:0
WEB_LINT_EXIT:0
VALIDATE_EXIT:0
GATES_EXIT:0
```

**The question for Emmz:** does the logo look right in the header and the tab, or should its size or spacing change?
The knobs are `.wordmark-logo` in `web/src/styles.css`: `height: 1.3em`, `margin-right: 0.3em` and
`vertical-align: -0.38em`.

## Recipe

From the repo root (the gates have already built the dashboard into `wwwroot`):

```sh
docker compose up -d --wait
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

Open <http://localhost:7480> and check:

1. **Sign-in page (before signing in):** the logo is just before `PANOPTICON` in the header, and the tab shows the
   eye icon.
2. **Sign in** as `dev@localhost.test`. The link is in the server's console output. On the dashboard, Members and
   Tokens, the logo is before the wordmark, and it sits centred on the capitals. The live status and account links
   still line up with the wordmark's text.
3. **Dark and light:** switch macOS Appearance (or DevTools → Rendering → `prefers-color-scheme`). Neither scheme
   shows a white or square edge around the tile.
4. **Phone width** (DevTools device toolbar, about 375 px): the header wraps, and the logo stays on the same line as
   `PANOPTICON`.
5. **Click the logo** on Members: it goes home to the dashboard.

## Next

- If Emmz approves, tick 3.3, write the section-3-done note, and commit §3 (README, tasks, notes). Then run the
  whole-change review and write the change-done note, offer code review, and offer `/opsx:archive`.
- If she asks for a tweak, change `.wordmark-logo` (and `web/DESIGN.md` "Logo" to match), rerun the web gates, and
  ask her to check again.
- Still for Emmz once merged: upload `docs/images/social-preview.png` under Settings → General → Social preview.
