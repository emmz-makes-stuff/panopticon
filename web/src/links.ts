import type { CardRepo } from "./types";

/** The link to a change's page (`/changes/<id>`), optionally opened at one document (`?doc=<path>`). */
export function changeHref(changeId: string, documentPath?: string): string {
  const base = `/changes/${encodeURIComponent(changeId)}`;
  return documentPath ? `${base}?doc=${encodeURIComponent(documentPath)}` : base;
}

/** Azure DevOps hosts, whose repo pages sit under `_git` (repos-page D2). */
function isAzureDevOps(host: string): boolean {
  return host === "dev.azure.com" || host.endsWith(".visualstudio.com");
}

/** A plain DNS hostname: labels of letters, digits and inner hyphens, joined by dots. No port, user info or path. */
const HOSTNAME =
  /^(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$/i;

/** The old Azure DevOps SSH host, whose remotes read `vs-ssh.visualstudio.com:v3/<org>/<project>/<repo>`. */
const OLD_AZURE_SSH = "vs-ssh.visualstudio.com";

/**
 * The host and owner segments a repo's web page lives at: the stored ones, lowercasing the host (the online MCP
 * endpoint stores whatever a reporter sends), except that the old Azure DevOps SSH host's `v3/<org>/…` owner moves
 * `<org>` into the host. The CLI renames only `ssh.dev.azure.com`, so those remotes arrive as they were (repos-page D2).
 */
function webLocation(repo: CardRepo): { host: string; owner: string[] } {
  const host = repo.host.toLowerCase();
  const owner = repo.owner.split("/");
  if (host === OLD_AZURE_SSH && owner[0] === "v3" && owner.length >= 3 && owner[1]) {
    return { host: `${owner[1].toLowerCase()}.visualstudio.com`, owner: owner.slice(2) };
  }

  return { host, owner };
}

/**
 * The repo's web page (spec repos, Repo web addresses; repos-page D2), built from its stored host, owner and name:
 * `https://<host>/<owner>/_git/<name>` on Azure DevOps, `https://<host>/<owner>/<name>` everywhere else. Each owner
 * segment and the name are encoded.
 *
 * The host and path are whatever a reporter sent, so a host that isn't a plain hostname (`github.com@evil.example`,
 * `evil.example/github.com`), or a `.` or `..` segment that the browser would resolve to another path, gets no link
 * rather than one that reads as one repo and opens something else: null (spec repos, Repos that get no link).
 */
export function repoWebUrl(repo: CardRepo): string | null {
  const { host, owner } = webLocation(repo);
  if (!HOSTNAME.test(host) || [...owner, repo.name].some((s) => s === "." || s === "..")) {
    return null;
  }

  const path = owner.map(encodeURIComponent).join("/");
  const name = encodeURIComponent(repo.name);
  const url = `https://${host}/${isAzureDevOps(host) ? `${path}/_git/${name}` : `${path}/${name}`}`;
  return new URL(url).hostname === host ? url : null;
}
