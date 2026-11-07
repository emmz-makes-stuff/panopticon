# ADR-0001: The CLI is a local MCP bridge in front of the online MCP endpoint

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** Emmz

## Context

Discovery (`docs/DISCOVERY.md`) requires:

- agents report progress through an **online MCP endpoint** (hard constraint);
- a change's documents **sync automatically on report**;
- **any MCP-capable agent** can report, so we can't rely on Claude Code hooks;
- a card leaves the live view when its change is **archived in OpenSpec**.

A remote MCP endpoint can't read an agent's local files, so read literally, "agents call the online
endpoint" and "docs sync automatically" conflict.

## Decision

The system has two parts:

1. **Server.** It hosts the dashboard and an **online MCP endpoint** (streamable HTTP, stateless). Its
   tools report progress for a change and accept a change's documents.
2. **CLI (`panopticon`).** Besides setup and manual sync, `panopticon mcp` runs as a **stdio MCP server**
   that agents configure. When the agent calls its report tool, the bridge:
   - resolves the repo identity (host + owner + repo) from the local git remote;
   - reads `openspec/changes/<change>/` (proposal, design, tasks, spec deltas, devlog) and syncs it;
   - notices when the change has moved to `openspec/changes/archive/` and reports it as archived;
   - forwards the report to the online MCP endpoint with the user's API token.

The online endpoint stays the single server-side API. The bridge is an MCP *client* of it.

## Consequences

- Sync is automatic for any MCP agent, and agents send no file contents, so it costs them no tokens.
- Repo identity and archive detection come from the filesystem and git, not from what the agent claims.
- The CLI must be installed on every VM. `panopticon setup` handles this, and Native AOT keeps it to a
  single binary with no runtime needed (ADR-0002).

## Alternatives rejected

- **Agent sends docs as tool arguments.** It burns tokens on every report, and the docs can be stale,
  partial or wrong. Repo identity and archive state would be self-reported.
- **Agent reports directly and runs `panopticon sync` separately.** Sync depends on the agent obeying
  instructions, or on hooks that only Claude Code has, so the bars go stale.
