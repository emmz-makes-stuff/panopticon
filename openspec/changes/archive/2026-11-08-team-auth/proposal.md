# Proposal

## Why

The walking skeleton proved the reporting path end to end, but anyone can read its dashboard, and every reporter
shares one bootstrap token. That's why it can't go on the internet. Panopticon is for a team from day one
(`docs/DISCOVERY.md`), so people need to sign in, and every report needs to belong to a team member. ADR-0005
fixes the shape: magic-link sign-in through Resend, and per-user API tokens issued by a device-style
`panopticon login`.

## What Changes

- **Team members.**
  - Only members can sign in.
  - Server config (`Panopticon:InitialMembers`) seeds the first members' emails.
  - Any signed-in member can invite a new member by email, and remove a member.
  - Each member has a display name, which they can change.
  - Removing a member ends their sessions and revokes their API tokens.
- **Dashboard sign-in.**
  - A member asks for a sign-in link and gets an email through Resend.
  - Each link works once and expires after 15 minutes.
  - Signing in starts a session cookie that lasts 7 days and is renewed while in use (sliding). Members can sign
    out.
  - The sign-in page answers the same way whether or not the email belongs to a member.
- **Development email.** In the Development environment with no Resend key, the server logs the sign-in link
  instead of sending it. Anywhere else, a missing Resend key or sender stops start-up.
- **The dashboard requires sign-in** (**BREAKING**). The app, `/api/changes` and `/api/events` all need a
  session.
- **Device login.**
  - `panopticon login` shows a short code and a URL.
  - A signed-in member approves the code in the dashboard, after seeing which machine is asking.
  - The CLI then receives a per-user API token. The token doesn't expire until it's revoked. It's stored hashed on
    the server, and in the user's CLI config locally.
  - Each code expires after 10 minutes.
- **A tokens page.** Each member sees their own tokens (machine, created, last used) and can revoke them.
- **Per-user bearer tokens on `/mcp`** (**BREAKING**).
  - Reports need a member's API token.
  - The shared `Panopticon:BootstrapToken` works only in the Development environment. If it's configured anywhere
    else, the server refuses to start.
- **Reports belong to a member.** The member comes from the token, so a reporter can't claim to be someone else.
  An agent is now a member's client on a machine, and each agent row on a card shows the member, for example
  "claude-code on vm-1 · Emmz".
- **The CLI bridge reads its configuration from the CLI config** that `panopticon login` writes.
  `PANOPTICON_SERVER_URL` and `PANOPTICON_TOKEN` still override it.

Not in this change: drill-down and the full document set, archive → history, filtering, idle/stale state,
metrics, `panopticon setup`, `panopticon logout`, GitHub Releases, and the Railway deployment. Roles are out
too: every member can do everything.

## Capabilities

### New Capabilities

- `team-members`: who belongs to the team. This covers seeding from config, inviting, removing, and display
  names. It also covers what removing a member revokes.
- `dashboard-sign-in`: how a member signs in to the dashboard. This covers magic-link requests and emails,
  single-use expiring links, sessions and signing out, Development-only link logging, and the rule that the
  dashboard and its APIs need a session.
- `reporter-credentials`: how a reporter gets and loses a credential. This covers the device-login flow (CLI and
  dashboard approval), per-user API tokens, and the tokens page with revocation.

### Modified Capabilities

- `progress-reporting`:
  - "Bearer token required" changes from the bootstrap token to per-user API tokens, with the bootstrap token
    allowed in Development only.
  - "Agents on a change" changes so that an agent is identified by its member as well as its client and
    machine.
- `live-dashboard`:
  - "Card content" adds the member on each agent.
  - "Unauthenticated in this change" is removed: `dashboard-sign-in` replaces it.
- `cli-bridge`:
  - "Configuration" reads the server URL and token from the CLI config, with the environment variables
    overriding it.
  - "Forwarding to the server" gets a revoked-token message that says to run `panopticon login`.

## Impact

- **Server** (`src/Panopticon.Server`):
  - new `Auth/` code: members, sign-in links, sessions, device authorizations, API tokens, and the
    authentication handlers;
  - an email sender abstraction with Resend, Development logging, and a test double;
  - new endpoints under `/api/auth`, `/api/members`, `/api/tokens` and `/api/device`;
  - an EF Core migration;
  - authorization on the dashboard endpoints;
  - member attribution in `ReportRecorder` and `ChangeCards`.
- **CLI** (`src/Panopticon.Cli`): a new `login` command, a config file, and new configuration rules for the
  bridge. All of it stays Native AOT safe.
- **Web** (`web/`):
  - a small client-side router;
  - sign-in, link-confirmation, device-approval, members and tokens pages;
  - handling of a 401 response;
  - the member shown on each agent.

  The new pages follow `web/DESIGN.md`.
- **Tests:** most existing server and bridge tests authenticate with the bootstrap token today. They move to
  per-user tokens created by test helpers.
- **Dependencies:**
  - NuGet: the official `Resend` .NET SDK.
  - Rate limiting and data protection ship with ASP.NET Core, so they add no package.
  - No new machine dependency for the gates. A Resend account and a verified sending domain are needed only for
    the human verification of real email, and for deployments.
- **Configuration:**
  - `Panopticon:InitialMembers`;
  - `Panopticon:PublicUrl`, the base for links in emails and device-login URLs;
  - `Panopticon:Resend:ApiKey` and `Panopticon:Email:From`;
  - `Panopticon:BootstrapToken`, which becomes Development-only.
- **Docs:** `docs/cli.md` (`panopticon login` and the config file) and the README. ADR-0005 already covers the
  decision, so there's no new ADR.
