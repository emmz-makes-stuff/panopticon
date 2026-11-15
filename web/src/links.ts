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

/**
 * The repo's web page (spec repos, Repo web addresses; repos-page D2), built from its stored host, owner and name:
 * `https://<host>/<owner>/_git/<name>` on Azure DevOps, `https://<host>/<owner>/<name>` everywhere else. The host is
 * as the CLI stored it (lowercased, with no user info or port); each owner segment and the name are encoded.
 */
export function repoWebUrl(repo: CardRepo): string {
  const owner = repo.owner.split("/").map(encodeURIComponent).join("/");
  const name = encodeURIComponent(repo.name);
  const path = isAzureDevOps(repo.host) ? `${owner}/_git/${name}` : `${owner}/${name}`;
  return `https://${repo.host}/${path}`;
}
