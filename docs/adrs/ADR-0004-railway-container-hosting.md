# ADR-0004: One container on Railway, with the CLI released on GitHub

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** Emmz

## Context

There's one team per deployment, so a deployment must be easy to stand up again. The dashboard is on the
internet, and the repo lives on GitHub.

## Decision

- The server and the built web UI ship as **one Docker image**. A multi-stage Dockerfile runs the Vite
  build, publishes the server, and the server serves the built UI from `wwwroot`.
- The **reference deployment is Railway**: one service built from the Dockerfile and deployed from
  GitHub, plus Railway Postgres and a Railway Bucket (ADR-0003). Secrets (database URL, S3 credentials,
  Resend API key) are Railway environment variables.
- *Amended 2026-11-10:* a **version tag** (`v*`) deploys it, not a push to a branch. Railway's GitHub
  trigger only watches branches, so a GitHub Actions workflow runs `railway up` on each tag with a project
  token. The service's settings are in `.railway/railway.ts` (Railway Infrastructure as Code; Config as
  Code's `railway.toml` is deprecated and stops working on 2026-12-01), and the service, Postgres and the
  Bucket share one region (US East).
- **CLI binaries** for linux-x64, linux-arm64, osx-arm64 and win-x64 are published as **GitHub
  Releases** assets.

## Consequences

- Another team can run its own instance by deploying the same image anywhere containers run. Railway is
  the reference deployment, not a lock-in.
- Release automation (CI matrix for AOT builds per OS) is needed later. It isn't part of the gates.

## Alternatives rejected

- **AWS ECS Fargate.** It fits existing tooling but is heavier and costs more for a small team.
- **Azure Container Apps.** It's viable, but Emmz chose Railway.
