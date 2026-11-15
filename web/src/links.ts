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

/**
 * The repo's web page (spec repos, Repo web addresses; repos-page D2), built from its stored host, owner and name:
 * `https://<host>/<owner>/_git/<name>` on Azure DevOps, `https://<host>/<owner>/<name>` everywhere else. Each owner
 * segment and the name are encoded.
 *
 * The host is whatever a reporter sent, so anything but a plain hostname (`github.com@evil.example`,
 * `evil.example/github.com`) gets no link, rather than one that reads as one repo and opens another site: null.
 */
export function repoWebUrl(repo: CardRepo): string | null {
  if (!HOSTNAME.test(repo.host)) {
    return null;
  }

  const owner = repo.owner.split("/").map(encodeURIComponent).join("/");
  const name = encodeURIComponent(repo.name);
  const path = isAzureDevOps(repo.host) ? `${owner}/_git/${name}` : `${owner}/${name}`;
  const url = `https://${repo.host}/${path}`;
  return new URL(url).hostname === repo.host.toLowerCase() ? url : null;
}
