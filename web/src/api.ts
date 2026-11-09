import type { FilterChoices } from "./filters";
import type { ChangeCard, ChangePage, HistoryPage, RenderedDocument } from "./types";

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

/** The header every state-changing `/api` call carries (design D6): another site's form or fetch can't send it. */
export const REQUEST_HEADER = "X-Panopticon-Request";

/** A failed API call: the HTTP status, and the server's problem `detail` (and `reason`, when it gives one). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly reason?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export interface Api {
  get<T>(path: string): Promise<T>;
  /** Resolves to the JSON body, or `undefined` for an empty answer (204). */
  send<T = undefined>(
    method: "POST" | "PATCH" | "DELETE",
    path: string,
    body?: unknown,
  ): Promise<T>;
}

export interface ApiOptions {
  fetch?: FetchLike | undefined;
  /** Called on every 401: the session has ended or never existed. The call still rejects, with an ApiError. */
  onSignedOut?: (() => void) | undefined;
}

/**
 * The one way the web app calls `/api` (design D18). It asks for JSON, sends the D6 header, and turns a 401 into
 * a "signed out" signal that the shell handles by showing the sign-in page.
 */
export function createApi(options: ApiOptions = {}): Api {
  const fetchFn = options.fetch ?? ((input, init) => fetch(input, init));

  async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { Accept: "application/json", [REQUEST_HEADER]: "1" };
    const init: RequestInit = { method, headers, credentials: "same-origin" };
    if (body !== undefined) {
      headers["Content-Type"] = "application/json";
      init.body = JSON.stringify(body);
    }

    const response = await fetchFn(path, init);
    if (response.status === 401) {
      options.onSignedOut?.();
      throw new ApiError(401, "You're signed out.");
    }

    if (!response.ok) {
      const problem = await readProblem(response);
      throw new ApiError(
        response.status,
        problem.detail ?? `${method} ${path} answered ${response.status}`,
        problem.reason,
      );
    }

    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  return {
    get: (path) => request("GET", path),
    send: (method, path, body) => request(method, path, body),
  };
}

async function readProblem(
  response: Response,
): Promise<{ detail: string | undefined; reason: string | undefined }> {
  try {
    const problem = (await response.json()) as { detail?: unknown; reason?: unknown } | null;
    return {
      detail: typeof problem?.detail === "string" ? problem.detail : undefined,
      reason: typeof problem?.reason === "string" ? problem.reason : undefined,
    };
  } catch {
    return { detail: undefined, reason: undefined };
  }
}

/** Every change the server knows about, most recently reported first (the server's order). */
export function fetchChanges(api: Api): Promise<ChangeCard[]> {
  return api.get<ChangeCard[]>("/api/changes");
}

/** A change's page: its card, archive state and document list. A 404 (an unknown change) rejects with an ApiError. */
export function fetchChange(api: Api, changeId: string): Promise<ChangePage> {
  return api.get<ChangePage>(`/api/changes/${encodeURIComponent(changeId)}`);
}

/** One of a change's documents, rendered by the server. Each path segment is encoded; the '/'s between them stay. */
export function fetchDocument(api: Api, changeId: string, path: string): Promise<RenderedDocument> {
  const encoded = path.split("/").map(encodeURIComponent).join("/");
  return api.get<RenderedDocument>(
    `/api/changes/${encodeURIComponent(changeId)}/documents/${encoded}`,
  );
}

/**
 * A page of archived changes, newest first; pass the previous page's `nextBefore` for the next one. `filters` is a
 * query string of history filters (from `toQuery`), which the server applies (filters-and-idle-cards D3).
 */
export function fetchHistory(api: Api, before?: string | null, filters = ""): Promise<HistoryPage> {
  const params = new URLSearchParams(filters);
  if (before) {
    params.set("before", before);
  }

  const query = params.toString();
  return api.get<HistoryPage>(query ? `/api/history?${query}` : "/api/history");
}

/** The history's filter choices: the values among archived changes. */
export function fetchHistoryChoices(api: Api): Promise<FilterChoices> {
  return api.get<FilterChoices>("/api/history/filters");
}

/** The signed-in member (`GET /api/session`). */
export interface SessionMember {
  id: string;
  email: string;
  displayName: string;
}
