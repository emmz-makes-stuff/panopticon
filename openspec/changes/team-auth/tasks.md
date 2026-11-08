# Tasks

## 1. Members and the data model

- [x] 1.1 Add the D1 tables (`members`, `sign_in_links`, `sessions`, `device_logins`, `api_tokens`) and the D2 change (`agents.member_id` plus the `NULLS NOT DISTINCT` unique index) in one EF Core migration, and verify a Testcontainers test applies it to an empty database and to one with walking-skeleton data
- [x] 1.2 Add the D3 secret helper (32-byte base64url secrets, the `pnp_` prefix for API tokens, SHA-256 hashes, user codes from `BCDFGHJKLMNPQRSTVWXZ` formatted `XXXX-XXXX` and normalized ignoring case and the hyphen), and verify unit tests for format, entropy length and normalization
- [x] 1.3 Seed `Panopticon:InitialMembers` at start-up (D10: normalized keys, default display name, skip any existing row, removed ones included) and fail start-up when no active member exists, and verify the "Members are identified by email" and "Initial members from configuration" scenarios as integration tests
- [x] 1.4 Make `Panopticon:BootstrapToken` optional and fail start-up when it's set outside Development (D5); add `Panopticon:PublicUrl`, then update `appsettings.Development.json` (public URL, the `dev@localhost.test` seed) and `PanopticonFactory` (seeded members, a `Settings` default for `InitialMembers`); verify start-up tests for each setting, and that the existing suite still passes

## 2. Email and magic-link sign-in

- [x] 2.1 Add `IEmailSender` with `ResendEmailSender` (`Resend` SDK pinned in `Directory.Packages.props`), the Development-only `LoggingEmailSender`, and a test `CapturingEmailSender`, with start-up validation of `Resend:ApiKey`, `Email:From` and `PublicUrl` outside Development (D9); verify the "Missing key in production" and "Local development" scenarios as tests
- [x] 2.2 Implement `POST /api/auth/sign-in` (always 202; link stored hashed; an email with a `/sign-in/confirm#<secret>` link only for active members; send failures logged without the link) and the D7 per-email and per-IP limits; verify "Member asks for a link", "Stranger asks for a link", "Delivery fails" and "Repeated requests" as integration tests
- [x] 2.3 Implement `POST /api/auth/confirm` (single use, 15-minute expiry, active member only) and the `Session` scheme (D4: hashed session cookie, HttpOnly, SameSite=Lax, Secure outside Development, a 7-day sliding expiry written at most once a minute); verify "Link used twice", "Expired link", "Link for a removed member", "Scanner opens the link" (a GET spends nothing), "Active use" and "Idle for a week" with `ManualClock`
- [x] 2.4 Add `GET /api/session` and `POST /api/auth/sign-out`, plus the D6 `X-Panopticon-Request` endpoint filter for every state-changing session endpoint; verify "Sign out" (an old cookie is refused) and "Forged request" (a missing header gets refused) as tests

## 3. Dashboard requires sign-in

- [ ] 3.1 Require a session on `/api/changes` and `/api/events`, with a 401 and no redirect for unauthenticated `/api/*` calls; verify the "Not signed in" scenario and that `/healthz`, the sign-in endpoints and the app shell stay open; update the existing dashboard endpoint tests to sign in through a test helper
- [ ] 3.2 Re-check the session on each SSE keep-alive tick and end the stream when it's gone (D11), and verify a test where signing out (and, separately, removing the member) ends an open `/api/events` stream within one tick

## 4. Member management API

- [ ] 4.1 Implement `GET /api/members`, `POST /api/members` (invite, or make a removed member active again; 400 for an invalid email, 409 for an active member) and `PATCH /api/members/me` (1–50 characters after trimming), and verify the "Invite a colleague", "Invalid address", "Already a member", "List members", "Default name", "Rename yourself" (in the list) and "Blank name rejected" scenarios
- [ ] 4.2 Implement `DELETE /api/members/{id}` (no self-removal; one transaction that sets `removed_at`, deletes sessions and revokes tokens), and verify "Remove a colleague", "Can't remove yourself" and that a removed member's sign-in requests send no email

## 5. Device login and API tokens (server)

- [ ] 5.1 Add the device-login and token request and response records to `Panopticon.Contracts` (with the source-generated JSON context), and verify a round-trip serialization test
- [ ] 5.2 Implement `POST /api/device/start` and `POST /api/device/poll` (pending, slow_down, denied, expired, and approved once, D12) with the D7 limits, and verify "Nobody approves" (server side), "Token handed over once" and "Polling too fast"
- [ ] 5.3 Implement `GET /api/device/{code}` and the approve and deny endpoints (session plus the D6 header; codes normalized; per-session attempt limit), and verify "Approve" and "Deny" (server side) and "Unknown or expired code"
- [ ] 5.4 Implement `GET /api/tokens` and `DELETE /api/tokens/{id}` (only your own tokens, 404 for anyone else's, no secret in any response), and verify "Token never shown again", "List and revoke" (server side) and "Someone else's token"

## 6. Per-user tokens on `/mcp` and attribution

- [ ] 6.1 Replace `BootstrapTokenHandler` as `/mcp`'s scheme with `ApiToken` (D4: hash lookup, unrevoked and active member, `last_used_at` written at most once a minute, the bootstrap token in Development only), confirm how the member reaches `ReportProgressTool` (D16), and verify the "Missing token", "Wrong token", "No token configured", "Revoked token", "Bootstrap token in Development", "Bootstrap token elsewhere" and "Last used" scenarios
- [ ] 6.2 Record the member on agents (`(member, client, machine)` identity) and add `Member` to `CardAgent`, and verify "Two members on one machine", "Card shows identity and agents" (API side), "Agent without a member" and "Reports remain" (a removed member's agent still shows their name)
- [ ] 6.3 Move the server and bridge test helpers (`PanopticonFactory`, `BridgeHarness`, the end-to-end check) from the bootstrap token to a member plus API token created through a test helper; keep the bootstrap tests under `Development`; and verify `make test` is green

## 7. CLI login and configuration

- [ ] 7.1 Add the D14 config file (path per OS, atomic write, mode 0600 and directory 0700 on Unix, source-generated JSON), and make `BridgeSettings` take each value from its environment variable, then the file; verify unit tests for "Saved login", "Environment overrides the saved login" and an unreadable file
- [ ] 7.2 Update the bridge's messages ("Missing token" naming `panopticon login`, and "Token rejected" saying to run `panopticon login`), and verify the updated bridge tests
- [ ] 7.3 Implement `panopticon login [--server <url>]` (D15: start, print the code and URL, poll with back-off, save on approval, exit 1 on denied or expired, 130 on cancel), and verify in-process tests against the test server for "Login on a VM", "Approve" (the CLI saves the token and prints the member), "Deny" and "Nobody approves" (with a short expiry), plus "Saved for the bridge" (file mode included)
- [ ] 7.4 Add `login` to `panopticon --help` and confirm it works in the Native AOT binary (a published-binary test of `login` against an unreachable server, which fails cleanly with no trimming warnings), and update `docs/cli.md` (login, the config file, precedence) and verify every documented command runs as written

## 8. Web: sign-in, pages and the card

- [ ] 8.1 Add the D18 router and the `api.ts` wrapper (the D6 header, and 401 → signed out), plus the session-first start-up in `main.ts` and the "signed out vs offline" check in `connectLive`, and verify Vitest tests for routing, the 401 handling and "Session ends while watching"
- [ ] 8.2 Build the sign-in pages (`/sign-in`: the request form and "check your email"; `/sign-in/confirm`: read the fragment, "Sign in as …?", confirm, and the used and expired messages with a resend), and verify Vitest tests for each state
- [ ] 8.3 Build the signed-in header (name, Members, Tokens, Sign out), and the members page (list, invite, rename yourself, remove others with a confirmation), with display names rendered as text; verify Vitest tests for each action and for markup in a name staying text
- [ ] 8.4 Build the device approval page (`/device?code=`: code entry, machine name, approve or deny, never on load, the code kept across sign-in) and the tokens page (list, revoke), and verify Vitest tests for "Not signed in" (the return trip), approve, deny, an invalid code, and revoke
- [ ] 8.5 Show the member on each card's agent row (D17) and update `web/DESIGN.md` for the new pages and the row, using the `frontend-design` skill; verify the card tests for "Card shows identity and agents" and "Agent without a member"

## 9. Integration and human verification

- [ ] 9.1 Extend the container end-to-end check: start with an initial member, sign in through captured or logged links, run `panopticon login` and approve, report through `panopticon mcp`, and see the card with the member's name; verify the check passes
- [ ] 9.2 Update the README (configuration settings, the first sign-in, `panopticon login`), and write the human verification recipe in a devlog note (real Resend delivery with a verified domain; the dashboard look and the new pages in a real browser; Claude Code reporting through a `panopticon login` token end to end); stop for Emmz to run it

## Workflow follow-up

- Run the whole-change review and offer `/code-review` before archiving.
- Archive with `/opsx:archive` once the review is done.
- Next changes, per the roadmap: drill-down, the full documents and archive → history; filtering and idle/stale;
  metrics; `panopticon setup`, Releases and the Railway deploy (which also configures forwarded headers and pins
  Postgres at 15 or later).
