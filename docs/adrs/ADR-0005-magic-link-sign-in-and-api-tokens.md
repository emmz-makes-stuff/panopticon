# ADR-0005: Magic-link sign-in via Resend, and per-user API tokens for reporters

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** Emmz

## Context

The team signs in from day one. Each agent/CLI reporter must be tied to a team member through a
credential issued at setup. Setup should take minutes. Repos can be on any git host, so git-host login
isn't a natural fit.

## Decision

- **Dashboard sign-in uses magic-link email**, sent through **Resend** (official Resend .NET SDK,
  `IResend`). Links are single-use and short-lived, and are stored hashed in Postgres. A session cookie
  follows. Only invited email addresses can sign in. How invitations work is left to the proposal.
- **Reporter credentials use a device-style flow.** `panopticon login` shows a code. The user approves it
  in the dashboard after a magic-link sign-in. The server issues a **per-user API token**, stored hashed
  server-side and in the user's CLI config locally. The bridge sends it as a bearer token to the online
  MCP endpoint.

## Consequences

- Each deployment needs a Resend API key and a verified sending domain.
- Nothing in auth is tied to a git host or an external identity provider.
- Token revocation and listing belong in the dashboard. Exactly where is left to the proposal.

## Alternatives rejected

- **Generic OIDC.** It's flexible, but each deployment would need an identity provider.
- **GitHub OAuth.** It conflicts with "any git host".
- **Built-in passwords.** We'd own password security.
