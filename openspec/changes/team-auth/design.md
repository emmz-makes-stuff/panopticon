# Design

## Context

See proposal.md for the why, and the delta specs for the behaviour. Today's state, from the archived
walking-skeleton change:

- **Server auth** is one `BootstrapTokenHandler` scheme, used for `/mcp` only. `ServerSetup` requires
  `Panopticon:BootstrapToken` at start-up. The dashboard endpoints (`/api/changes`, `/api/events`) and the app
  shell are open.
- **Data** (EF Core 10, Npgsql, snake_case naming). There are `repos`, `changes`, `agents` (unique on
  client + machine keys), `change_agents` and `report_events`, each with lower-cased `*_key` identity
  columns.
- **CLI** (Native AOT). `panopticon mcp` reads `PANOPTICON_SERVER_URL` and `PANOPTICON_TOKEN` through
  `BridgeSettings`. Its HTTP calls are the MCP client's. There's no other command.
- **Web.** One view, the card grid, built by `mountApp` plus `connectLive`. There's no router. `web/DESIGN.md`
  is the "lit cells" design system.
- **Tests.** `PanopticonFactory` runs the server in the `Testing` environment, with the bootstrap token as the
  default credential. So do most server tests and bridge tests.

Binding decisions from the ADRs still apply. ADR-0005 decides magic links via Resend and device-style per-user
tokens. ADR-0002 decides .NET 10 and vanilla TypeScript. ADR-0003 decides Postgres plus S3.

## Goals / Non-Goals

**Goals:**

- Every secret is stored only as a hash: sign-in links, session ids, device secrets and API tokens.
- Each of them is single-purpose and revocable on the server.
- One way to authenticate per surface: session cookies for the browser and `/api/*`, bearer API tokens for
  `/mcp`.
- No sign-in state lives only in the cookie. Ending a session or removing a member takes effect on the next
  request.
- The CLI stays one Native AOT binary with no new runtime dependency.

**Non-Goals:**

- No roles or permissions beyond "is an active member".
- No invitation emails. An invited person just requests a sign-in link.
- No email change for an existing member.
- No `panopticon logout`. Tokens are revoked from the dashboard.
- No ASP.NET Core Identity.
- No multi-instance coordination for rate limits. A Railway deployment is one container (ADR-0004).

## Decisions

### Data model (one EF Core migration)

- **D1. New tables.** Each new table has lower-cased key columns and unique indexes in the style of D6 from
  walking-skeleton.
  - `members`: `id`, `email`, `email_key` (unique), `display_name`, `created_at`, `removed_at`,
    `invited_by_member_id`.
  - `sign_in_links`: `id`, `member_id`, `secret_hash` (unique), `created_at`, `expires_at`, `used_at`.
  - `sessions`: `id`, `member_id`, `secret_hash` (unique), `created_at`, `last_seen_at`, `expires_at`.
  - `device_logins`: `id`, `device_secret_hash` (unique), `user_code` (unique while pending), `machine_name`,
    `created_at`, `expires_at`, `last_polled_at`, `approved_by_member_id`, `approved_at`, `denied_at`,
    `token_id`, `token_delivered_at`.
  - `api_tokens`: `id`, `member_id`, `secret_hash` (unique), `machine_name`, `created_at`, `last_used_at`,
    `revoked_at`.

  A removed member keeps their row (`removed_at` set). Seeding and reports keep working that way, and so does
  "Reports remain".
- **D2. `agents.member_id`** is nullable. It's null for bootstrap reports and for rows from before this
  change. The unique index becomes `(member_id, client_name_key, machine_name_key)` with `NULLS NOT DISTINCT`
  (Postgres 15+; compose and the tests use 17). Existing agents keep `member_id = null` and stay as they are.
  `report_events` needs no change: it points at the agent, which points at the member.
- **D3. Secrets.**
  - Every secret is 32 bytes from `RandomNumberGenerator`, base64url-encoded, with a readable prefix:
    `pnp_` for API tokens, and none for the others.
  - Each is stored as a SHA-256 hash and looked up by that hash. They're high-entropy, so a slow KDF adds
    nothing. Hashed lookup also means no secret is ever compared character by character.
  - Device user codes are the exception. They're 8 letters from `BCDFGHJKLMNPQRSTVWXZ`, 20⁸ ≈ 2.6×10¹⁰
    combinations, shown as `XXXX-XXXX`. They're short-lived, single-use and rate-limited, and they're never
    enough on their own: the token goes only to the device secret.
  - Rejected: ASP.NET Data Protection-encrypted tokens. They can't be revoked one at a time without a table
    anyway.

### Server authentication

- **D4. Two authentication schemes, plus a policy scheme that picks between them.**
  - `Session` (cookie `panopticon_session`; HttpOnly; `SameSite=Lax`; `Secure` outside Development; path `/`)
    resolves the session row on each request.
    - It refuses a session that has expired or whose member has been removed.
    - It slides `expires_at` to now + 7 days. It writes at most once a minute per session, so a busy dashboard
      doesn't write on every request.
    - Rejected: a plain cookie-auth ticket. Revoking it on removal or sign-out would need a server-side
      check anyway.
  - `ApiToken` (bearer) looks up `api_tokens` by hash and needs the token unrevoked and its member active.
    - It throttles `last_used_at` the same way, once a minute.
    - In Development only, it also accepts the bootstrap token, with the existing constant-time compare. That
      gives a principal with no member id.
  - `/mcp` requires `ApiToken`, and every `/api/*` endpoint requires `Session` (except sign-in and the device
    start and poll endpoints). Unauthenticated `/api/*` calls get a 401, never a redirect: the SPA handles
    it.
- **D5. Bootstrap token rules.**
  - `Panopticon:BootstrapToken` is optional now.
  - When it's set outside Development, `ValidateOnStart` fails naming it.
  - The `Testing` environment that `PanopticonFactory` uses counts as "outside Development". Tests that cover
    the bootstrap token run the factory in `Development`.
- **D6. Cross-site protection.** State-changing session endpoints need two things:
  - an `X-Panopticon-Request: 1` header, which a cross-site form can't send and a cross-site `fetch` can't
    send without a CORS preflight that the server never allows;
  - `SameSite=Lax`.

  This is checked by one endpoint filter. Rejected: ASP.NET antiforgery tokens. With a JSON-only SPA and no
  CORS, they'd add a token endpoint and client plumbing for no extra protection.
- **D7. Rate limits** use the built-in `Microsoft.AspNetCore.RateLimiting`, kept in memory (one instance, per
  the Non-Goals).
  - **Sign-in requests:** 5 per 15 minutes per email key, and 20 per 15 minutes per client IP.
  - **Device start:** 10 per 15 minutes per IP.
  - **Device poll:** one per interval per device secret. Polling faster gets a `slow_down` result rather than
    a 429, so the CLI can back off.
  - **Code approval attempts:** 10 per minute per session.

  The client IP comes from the connection. Forwarded headers are configured with the Railway deployment
  change, which is out of scope.

### Sign-in flow

- **D8. Endpoints.**
  - `POST /api/auth/sign-in {email}` always answers 202. When the address is an active member, and within the
    limit, it stores a link and sends the email.
  - The email links to `{PublicUrl}/sign-in/confirm#{secret}`. The secret goes in the URL fragment, so it
    isn't sent in requests, server logs or `Referer`.
  - That SPA page shows "Sign in as …?" and a button. The button posts `POST /api/auth/confirm {secret}`,
    which uses the link, creates the session and sets the cookie. A GET never spends a link: that's what
    "Confirming before signing in" needs, so mail scanners can't spend it.
  - `POST /api/auth/sign-out` deletes the session row and clears the cookie.
  - `GET /api/session` returns the signed-in member (id, email, display name), or 401. The app shell calls it
    first.
- **D9. Email sending** goes through an `IEmailSender`.
  - `ResendEmailSender` uses the official `Resend` SDK's `IResend`, configured with `Panopticon:Resend:ApiKey`.
    The sender is `Panopticon:Email:From`.
  - `LoggingEmailSender` is used only when the environment is Development and no key is set. It logs the
    link at Warning, so it stands out.
  - Tests register a `CapturingEmailSender`.
  - Each email has a plain-text part and a minimal HTML part, with no tracking.
  - A send failure is logged without the address's secret link, and the 202 still goes back.
  - Outside Development, `PublicUrl`, `ApiKey` and `From` are all validated at start-up.
  - `appsettings.Development.json` sets `PublicUrl` to `http://localhost:7480` and seeds `dev@localhost.test`.

### Members

- **D10. Seeding.**
  - At start-up, after migrations, a hosted step inserts each `Panopticon:InitialMembers` address that has no
    row. Addresses are normalized with trim and lower-case for the key. The display name defaults to the local
    part.
  - It then fails start-up if no active member exists.
  - Email validity is checked with `System.Net.Mail.MailAddress` plus a rule of "one `@`, no whitespace". It's
    deliberately loose: the real test is whether the member can sign in.
- **D11. Member endpoints.** All of them need a session, and the state-changing ones need the D6 header.
  - `GET /api/members` lists the active members.
  - `POST /api/members {email, displayName?}` invites, or makes a removed member active again. It answers 409
    if the address is already active, and 400 for an invalid email.
  - `PATCH /api/members/me {displayName}` renames you.
  - `DELETE /api/members/{id}` removes a member. You can't remove yourself (400). Removal runs in one
    transaction: it sets `removed_at`, deletes their sessions, and revokes their tokens.
  - Removing a member also closes any of their open `/api/events` streams. The SSE endpoint re-checks its
    session about every keep-alive tick (25 s), so a removed or signed-out member's stream ends within about
    that time. The dashboard then gets a 401 when it reconnects and shows the sign-in page.

### Device login and tokens

- **D12. Device flow**, an RFC 8628-shaped flow without the OAuth framing.
  - `POST /api/device/start {machineName}` is open, rate-limited, and accepts a machine name of at most 100
    characters. It returns `{deviceSecret, userCode, approveUrl, intervalSeconds: 5, expiresInSeconds: 600}`.
  - `POST /api/device/poll {deviceSecret}` is open. It returns one of `pending`, `slow_down`, `denied`,
    `expired`, or `approved` with `{token, member: {displayName, email}}`. The `approved` answer comes once:
    the token is created and its secret returned in the same request that sets `token_delivered_at`. Later
    polls return `expired`.
  - `GET /api/device/{userCode}` needs a session. It returns the machine name and the state, for the approval
    page.
  - `POST /api/device/{userCode}/approve` and `/deny` need a session and the D6 header.
  - The approval page is `/device?code=XXXX-XXXX`. When it isn't signed in, it sends you through sign-in and
    keeps the code as the D18 return path, so it survives the magic-link round trip. It never approves on load.
- **D13. Token endpoints.**
  - `GET /api/tokens` lists your active tokens.
  - `DELETE /api/tokens/{id}` revokes one of your tokens. It answers 404 for anyone else's, so it doesn't
    reveal that the token exists.
- **D14. CLI config file.**
  - The file is `$XDG_CONFIG_HOME/panopticon/config.json`, defaulting to `~/.config/panopticon/config.json`,
    or `%APPDATA%\panopticon\config.json` on Windows. It holds `{serverUrl, token}`.
  - It's written atomically: written to a temp file next to it, then moved over it.
  - On Unix the file mode is 0600 and the directory is 0700, set before writing. On Windows the default user
    profile ACLs are relied on.
  - JSON is source-generated, for AOT.
  - `BridgeSettings` resolves each value from its environment variable, then the file. The saved token is used
    only when the server in use is the saved server, compared on scheme, host, port and base path, ignoring case,
    a trailing `/` and `/mcp`. Otherwise it would be sent to a server that never issued it (found in the
    whole-change review). A missing or
    unreadable file means "not set". An unreadable file is logged once to stderr without its content.
- **D15. `panopticon login [--server <url>]`.**
  - The server comes from the flag, then `PANOPTICON_SERVER_URL`, then the saved config. With none of them,
    it's an error that says to pass `--server`.
  - It posts `start` with `Environment.MachineName` and prints the code and the approval URL to stdout. It
    doesn't open a browser: VMs have none.
  - It polls at the stated interval, doubling the interval on `slow_down`. It saves on `approved`, and exits 1
    on `denied` or `expired`, or 130 on Ctrl+C.
  - It uses a plain `HttpClient` with the Contracts JSON context. The device request and response records go
    into `Panopticon.Contracts`, so the server and CLI share them, as D15 in walking-skeleton did for the
    report.

### Attribution and the card

- **D16. Member from the principal.**
  - `ReportProgressTool` reads the member id from the request's `ClaimsPrincipal`. That's the MCP request
    context's user, or `IHttpContextAccessor` if the stateless HTTP transport doesn't flow it. §6.1 checks
    which, with a test.
  - It passes the member to `ReportRecorder`, which finds or adds the agent by `(member, client, machine)`.
  - `CardAgent` gains `Member` (the display name, nullable).
  - The live broadcast after a rename isn't needed: names refresh on the next report or page load. That
    matches "Rename yourself" without new events.
- **D17. Card row wording.**
  - The card shows the member's display name as its own, quieter element after the agent: `claude-code@vm-1`
    then `Emmz`.
  - It doesn't use Emmz's middot example ("claude-code on vm-1 · Emmz") as written: `web/DESIGN.md` bans
    middot-joined meta and words agents as `claude-code@vm-1`.
  - The final look of the new pages and this row goes through the `frontend-design` skill, then Emmz's own
    check.

### Web

- **D18. A tiny router.**
  - `history.pushState`, a path → view map, and link interception for `<a data-route>`. There's no
    framework (ADR-0002).
  - The views are `/` (cards), `/sign-in`, `/sign-in/confirm`, `/device`, `/members` and `/tokens`.
  - On load, the app calls `GET /api/session`.
    - On a 401 it shows `/sign-in`, remembering where you were going. The return path is kept in
      `localStorage` as `{path, savedAt}`, because the emailed link usually opens in a new tab, and a new tab's
      `sessionStorage` starts empty. It's removed when read, it's ignored after 15 minutes (a sign-in link's lifetime, and longer than a
      device code lives), and only a same-site path (`/…`, not `//…`) is followed.
    - Signed in, it shows a header with your name, links to Members and Tokens, and Sign out.
  - All `/api` calls go through one `api.ts` wrapper. It adds the D6 header and `Accept: application/json`,
    and turns a 401 into a "signed out" event, which the router handles.
  - `connectLive` doesn't open the stream until the session is known. When an `EventSource` error happens, a
    `GET /api/session` check tells "signed out" apart from "offline".

### Gates

Unchanged from walking-skeleton (`make gates`). The Resend SDK is a NuGet package pinned in
`Directory.Packages.props`, with no new machine dependency.

## Risks / Trade-offs

- [Email deliverability and real Resend behaviour can't be tested in the gates] → The sender is behind
  `IEmailSender`, and tests capture links. Real delivery is a human verification step, with a recipe in the
  last section.
- [In-memory rate limits reset on restart and don't span instances] → There's one container (ADR-0004). This
  is recorded as a Non-Goal, and the limits are tight enough that a restart doesn't help an attacker much.
- [A user code is phishable: an attacker starts a login and tricks a member into approving it] → The approval
  page shows the machine name and needs an explicit click. Codes expire in 10 minutes. The tokens page shows
  every token, so a rogue one can be revoked.
- [Session checks hit the database on every `/api/*` request] → Lookups go through the unique index on
  `secret_hash`, and `last_seen_at` writes once a minute at most. That's fine for a single team.
- [Most existing tests use the bootstrap token] → A task moves `PanopticonFactory` and `BridgeHarness` to
  helpers that create a member and an API token directly in the database. Only the bootstrap tests switch to
  Development.
- [An SSE stream outlives a removed member's session for up to one keep-alive tick] → This is accepted, and
  stated in D11.
- [The `NULLS NOT DISTINCT` index needs Postgres 15+] → Compose and the tests use 17. The deployment change
  will pin Railway's Postgres at 15 or later.

## Migration Plan

- The single migration adds the new tables, `agents.member_id`, and the replacement unique index. Existing
  rows stay valid with `member_id = null`.
- Deploying the change needs these settings:
  - `Panopticon:InitialMembers`;
  - `Panopticon:PublicUrl`;
  - `Panopticon:Resend:ApiKey` and `Panopticon:Email:From`, with a domain verified in Resend;
  - removing `Panopticon:BootstrapToken` outside Development.
- Agents switch from `PANOPTICON_TOKEN=<bootstrap>` to `panopticon login`.
- Rollback means reverting the deploy. The migration is additive apart from the index swap, which the
  migration's `Down` reverses.

## Open Questions

- What exactly the sign-in email says (subject and body text). It can be settled while building §2, without
  changing any spec.
