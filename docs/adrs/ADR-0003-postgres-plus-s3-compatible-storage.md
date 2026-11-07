# ADR-0003: PostgreSQL for structured data, S3-compatible object storage for documents

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** Emmz

## Context

The server keeps two kinds of data:

- **Structured data that changes often:** users, single-use magic-link tokens, per-user API tokens,
  changes and their agents, and a stream of report events. These feed idle/stale state, filtering,
  history and metrics, which are in v1.
- **Document blobs:** each change's Markdown files, synced on every report and kept for history.

Emmz first proposed putting all data in an S3 object store. An object store has no queries and no
transactions, and conditional writes on Railway Buckets couldn't be confirmed. That would force a single
instance with in-memory state, which is unsafe during Railway's overlapping zero-downtime deploys.

## Decision

- **PostgreSQL** (Railway Postgres in the reference deployment) holds all structured data, accessed
  through **EF Core 10 + Npgsql** with code-first migrations.
- **Any S3-compatible object store** holds document blobs, configured per deployment with endpoint,
  credentials, bucket and path-style flag. A **Railway Bucket** is the default. Access is through
  `AWSSDK.S3` with `ServiceURL`. Buckets are private: the server reads blobs and renders them, and never
  hands out bucket URLs.

## Consequences

- Filters, history and metrics are ordinary queries, and multiple instances are safe.
- Each deployment runs one more managed service (Postgres).
- Integration tests run against real Postgres and MinIO through Testcontainers, so the test gate needs
  Docker.

## Alternatives rejected

- **Bucket only, single instance.** No queries or transactions, a home-grown in-memory index, and
  writers can collide during deploys.
- **SQLite on a Railway volume + bucket.** A volume pins the service to one replica, and volume-backed
  redeploys have brief downtime.
