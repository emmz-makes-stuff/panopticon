/** The link to a change's page (`/changes/<id>`), optionally opened at one document (`?doc=<path>`). */
export function changeHref(changeId: string, documentPath?: string): string {
  const base = `/changes/${encodeURIComponent(changeId)}`;
  return documentPath ? `${base}?doc=${encodeURIComponent(documentPath)}` : base;
}
