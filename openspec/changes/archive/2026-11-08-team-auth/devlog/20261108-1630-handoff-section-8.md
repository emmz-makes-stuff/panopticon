# Handoff: §8 part-built, with 8.1–8.4 code and tests done and uncommitted; 8.5 styles and design remain

<!-- Section: 8 · Tasks: 8.1–8.4 built (unticked), 8.5 half · Base: d619181 -->

(File-name time: set to sort after `20261108-1625`. The real clock read 16:16 UTC.)

Context passed 40% mid-section, so this is a mid-section handoff. **Nothing in §8 is committed, and nothing is
ticked.** The section commits once, after 8.5 and the gates.

## Uncommitted work (web/ only), and its state

All of this is done and tested: `npx tsc --noEmit` is clean, ESLint is clean, Prettier is written, and Vitest
passes 92/92. `make gates` hasn't run for §8 yet.

- `src/api.ts` (rewritten): `createApi({fetch, onSignedOut})` → `Api {get, send}`.
  - It sends `Accept: application/json` and `X-Panopticon-Request: 1` on every call, and JSON bodies.
  - A 401 calls `onSignedOut` and throws `ApiError(401)`.
  - Other failures throw `ApiError(status, problem.detail, problem.reason)`. 204 resolves to undefined.
  - `fetchChanges(api)`. `SessionMember` type.
- `src/router.ts` (new): D18 router.
  - `createRouter({root, routes, fallback, guard, history, location, events})`.
  - It uses pushState and replaceState, intercepts `a[data-route]` clicks, and handles popstate.
  - The guard can redirect. A view returns a `Teardown | undefined`. A ticket drops a stale view's teardown.
- `src/shell.ts` (new): `startShell(root, deps)`.
  - The header holds the `a.wordmark` (`data-route`, inside `h1.wordmark-heading`), a `.status-slot`, and
    `nav.account` (the name as text, Members, Tokens, a Sign out button).
  - It calls `GET /api/session` first, then starts the router.
  - Guard: signed out on a non-public path → remember `path+search` in sessionStorage
    `panopticon.returnTo`, then go to `/sign-in`. Signed in on `/sign-in` → `/`.
  - Any later 401 remembers the page and goes to `/sign-in`.
  - `Env` is passed to views: `api`, `member()`, `signedIn()`, `renamed()`, `statusSlot`, `session`/`local`
    storage, `location` (with `hash` and `origin`), `history`, `now`, `createEventSource`, `confirm`.
  - `PUBLIC_PATHS` is `/sign-in` and `/sign-in/confirm`.
- `src/views/cards.ts`: `mountApp` with `statusSlot`, plus `connectLive` with `checkSignedIn` (a
  `GET /api/session` check, where a 401 means false). The teardown stops the stream and the 30 s timer.
- `src/views/signIn.ts`:
  - `/sign-in`: the form → `POST /api/auth/sign-in` → "Check your email" (it reads the same for strangers). It
    remembers the address in localStorage `panopticon.signInEmail`.
  - `/sign-in/confirm#secret`: it strips the fragment with `replaceState` and posts nothing on load. The heading
    is "Sign in as <email>?" if this browser asked, else "Sign in to a PANOPTICON?". The button calls
    `POST /api/auth/confirm` → `env.signedIn` → the return path or `/`.
  - On a 400 it shows the server's detail and a "Send me a new link" link to `/sign-in`.
- `src/views/members.ts`, `tokens.ts` and `device.ts`: 8.3 and 8.4 as the tasks describe.
  - Removing a member and revoking a token go through `env.confirm`.
  - Device: a GET shows "Log in vm-1?" with Approve and Deny buttons, and never decides on load. A 404 or a state
    other than `pending` shows "That code isn't valid…" and the code form.
- `src/dom.ts` (new): `el`, `button`, `alertLine`, `page`, `field`, `routeLink`. Strings always go in as text.
- `src/app.ts`:
  - `AppDeps` gains `api` and `statusSlot`. With a slot, it doesn't draw its own header.
  - The empty-state command is now `panopticon login --server <origin>` plus
    `claude mcp add panopticon --scope local -- panopticon mcp`.
- `src/live.ts`: the `checkSignedIn` option. On error: if signed out, close and stop for good. Otherwise the old
  retry.
- `src/card.ts` and `types.ts`: `CardAgent.member?: string | null`. The row is `claude-code@vm-1` then
  `span.card-agent-member` (D17).
- `src/format.ts`: `calendarDate(iso)` gives "8 Nov 2026" (en-GB, UTC).
- `src/main.ts`: just `startShell(root)`.
- Tests:
  - `api.test.ts`, `router.test.ts`, `shell.test.ts` ("Not signed in", the header, "Session ends while
    watching", the offline reconnect, sign out, and a 401 anywhere).
  - `views/signIn`, `members`, `device` (including the "Not signed in" return trip) and `tokens` tests.
  - New `card.test.ts` cases for the member and "Agent without a member".
  - Helpers live in `src/testing.ts` (`fakeServer`, `fakeNavigation`, `memoryStorage`, `MEMBER`, `until`, `q`)
    and `src/testShell.ts` (`startTestShell`, `signedIn`, `signedOut`). Don't import a `*.test.ts` from another
    test: it re-registers that file's tests.

## Decisions (flag to Emmz in the §8 report)

- **"Sign in as …?":** the server can't say whose link it is before the confirm, and D8 has no peek endpoint. The
  page names the address only when this browser asked for the link (localStorage), and otherwise says "Sign in to a
  PANOPTICON?". Nothing is added to the link or the server. It's copy, so it's easy to change.
- One sessionStorage return path serves both D18's "remember where you were going" and D12's "keep the code across
  sign-in". It survives the magic-link round trip **only if the link opens in the same tab or browser session**.
  The human recipe in 9.2 should check this.
- **The confirm page strips `#secret` from the URL.** Reloading it afterwards then shows "isn't complete".

## Gotchas

- `exactOptionalPropertyTypes` is on: optional option fields need `| undefined` to accept a passed `undefined`.
- ESLint forbids `!` (use `q()` in tests) and `void` in union types.

## Next

- **Finish 8.5:**
  - Run the `frontend-design` skill. Then write CSS in `web/src/styles.css` for:
    - the new header: `.wordmark` is now an `<a>` inside `h1.wordmark-heading`, plus `.status-slot` and
      `.account` with `.account-name`, the links and `.link-button`;
    - `.page`, `.field`/`.field-label`, `.primary`, `.danger-button`, `.form-error`, `.form-status`;
    - `.member-list`/`.member*`, `.token-list`/`.token*`, `.device-code`, `.actions`, and
      `.card-agent-member` (a quieter element, D17).
  - Check that the old `.wordmark` styles still apply to the link.
  - Update `web/DESIGN.md` for the new pages, the header and the agent row. Its "Empty dashboard" line still
    describes the old `claude mcp add` command.
  - The look is a human check: write the recipe into 9.2's verification note rather than stopping now.
- Then self-review `git diff d619181` plus the untracked files, run `make gates`, tick 8.1–8.5, write the §8-done
  note, and make one commit `feat(team-auth): web sign-in, pages and the card (§8)`.
- Then §9.1.
- The branch is unpushed (no upstream), and nothing is running.
