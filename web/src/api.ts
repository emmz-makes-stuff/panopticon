import type { ChangeCard } from "./types";

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

/** Every change the server knows about, most recently reported first (the server's order). */
export async function fetchChanges(fetchFn: FetchLike = fetch): Promise<ChangeCard[]> {
  const response = await fetchFn("/api/changes", { headers: { Accept: "application/json" } });
  if (!response.ok) {
    throw new Error(`GET /api/changes answered ${response.status}`);
  }

  return (await response.json()) as ChangeCard[];
}
