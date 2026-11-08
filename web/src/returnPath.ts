import type { StorageLike } from "./shell";

/**
 * Where the app goes after sign-in: the page you were sent away from (design D18, and D12's `/device?code=`).
 *
 * It's kept in localStorage, not sessionStorage, because the sign-in link arrives by email and mail apps open it in a
 * new tab, which starts with an empty sessionStorage. localStorage is shared by every tab of this origin.
 */
export const RETURN_TO_KEY = "panopticon.returnTo";

/** A saved path older than this is ignored: as long as a sign-in link lives, and longer than a device code (10 minutes). */
export const RETURN_TO_LIFETIME_MS = 15 * 60 * 1000;

interface Saved {
  path: string;
  savedAt: number;
}

export function saveReturnPath(storage: StorageLike, path: string, now: Date): void {
  const saved: Saved = { path, savedAt: now.getTime() };
  try {
    storage.setItem(RETURN_TO_KEY, JSON.stringify(saved));
  } catch {
    // Storage can be unavailable (blocked, or full). Then sign-in still works and lands on the cards.
  }
}

/**
 * The saved path, removed as it's read so it's used once. Null when there's none, it has expired, or it isn't a path
 * on this site (an open redirect otherwise: anything that can write localStorage could aim the post-sign-in hop).
 */
export function takeReturnPath(storage: StorageLike, now: Date): string | null {
  let raw: string | null;
  try {
    raw = storage.getItem(RETURN_TO_KEY);
    storage.removeItem(RETURN_TO_KEY);
  } catch {
    return null; // Unavailable storage: see saveReturnPath.
  }

  const saved = parse(raw);
  if (!saved || now.getTime() - saved.savedAt > RETURN_TO_LIFETIME_MS) {
    return null;
  }

  return saved.path.startsWith("/") && !saved.path.startsWith("//") ? saved.path : null;
}

function parse(raw: string | null): Saved | null {
  if (!raw) {
    return null;
  }

  try {
    const value = JSON.parse(raw) as Partial<Saved> | null;
    return typeof value?.path === "string" && typeof value.savedAt === "number"
      ? { path: value.path, savedAt: value.savedAt }
      : null;
  } catch {
    return null;
  }
}
