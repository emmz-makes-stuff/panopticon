// Test helpers (imported by *.test.ts only).
import type { FetchLike } from "./api";
import type { EventSourceLike } from "./live";
import type { ChangeCard } from "./types";

export const NOW = new Date("2026-11-08T12:00:00Z");

export function card(overrides: Partial<ChangeCard> = {}): ChangeCard {
  return {
    changeId: "c1",
    repo: { host: "github.com", owner: "emmz", name: "panopticon" },
    change: "walking-skeleton",
    agents: [{ client: "claude-code", machine: "vm-1", lastReportedAt: "2026-11-08T11:57:00Z" }],
    currentSection: { number: 2, title: "Core", progress: { done: 2, total: 5 } },
    overall: { done: 7, total: 20 },
    lastReportedAt: "2026-11-08T11:57:00Z",
    // A bootstrap reporter's change by default: no Project label, so other tests' text stays as it was.
    project: null,
    personal: false,
    projectTags: [],
    personalTags: [],
    ...overrides,
  };
}

export interface FakeFetch extends FetchLike {
  /** Every URL asked for, in order. */
  urls: string[];
  /** What the next `GET /api/changes` answers; change it to move the "server" on. */
  cards: ChangeCard[];
  status: number;
}

/** A fetch that answers `GET /api/changes` with its current `cards`, and records the URLs it was asked for. */
export function fakeFetch(cards: ChangeCard[], status = 200): FakeFetch {
  const fake: FakeFetch = Object.assign(
    (input: string) => {
      fake.urls.push(input);
      return Promise.resolve(new Response(JSON.stringify(fake.cards), { status: fake.status }));
    },
    { urls: [] as string[], cards, status },
  );

  return fake;
}

/** A controllable stand-in for the browser's EventSource. */
export class FakeEventSource implements EventSourceLike {
  static readonly instances: FakeEventSource[] = [];

  readyState = 0;
  onopen: ((event: Event) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  private readonly listeners = new Map<string, ((event: MessageEvent<string>) => void)[]>();

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  static latest(): FakeEventSource {
    const latest = FakeEventSource.instances.at(-1);
    if (!latest) {
      throw new Error("No EventSource was created");
    }

    return latest;
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close(): void {
    this.closed = true;
    this.readyState = 2;
  }

  open(): void {
    this.readyState = 1;
    this.onopen?.(new Event("open"));
  }

  emit(type: string, data: string): void {
    const event = new MessageEvent<string>(type, { data });
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }

  /** A dropped connection: 0 (CONNECTING) when the browser will retry, 2 (CLOSED) when it gives up. */
  fail(readyState: 0 | 2): void {
    this.readyState = readyState;
    this.onerror?.(new Event("error"));
  }
}

/** Lets pending promise callbacks (a refresh's fetch and render) run. */
export const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

/** One call the fake server received. */
export interface ServerCall {
  method: string;
  path: string;
  /** The path with its query, as asked for. */
  url: string;
  headers: Record<string, string>;
  body: unknown;
}

export type ServerAnswer = { status: number; body?: unknown };

export interface FakeServer extends FetchLike {
  calls: ServerCall[];
  /** Answers `method path` (path without its query) with a fixed answer, or one computed from the call. */
  on(
    method: string,
    path: string,
    answer: ServerAnswer | ((call: ServerCall) => ServerAnswer),
  ): FakeServer;
  /** The calls to `method path`. */
  to(method: string, path: string): ServerCall[];
}

/** A fetch that routes by method and path. Anything not set up answers 404. */
export function fakeServer(): FakeServer {
  const routes = new Map<string, ServerAnswer | ((call: ServerCall) => ServerAnswer)>();
  const server = Object.assign(
    (input: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      const path = input.split("?")[0] ?? input;
      const call: ServerCall = {
        method,
        path,
        url: input,
        headers: { ...(init?.headers as Record<string, string> | undefined) },
        body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined,
      };
      server.calls.push(call);
      const route = routes.get(`${method} ${path}`);
      const answer =
        typeof route === "function"
          ? route(call)
          : (route ?? { status: 404, body: { detail: "Not found." } });
      return Promise.resolve(
        new Response(answer.body === undefined ? null : JSON.stringify(answer.body), {
          status: answer.status,
        }),
      );
    },
    {
      calls: [] as ServerCall[],
      on(
        method: string,
        path: string,
        answer: ServerAnswer | ((call: ServerCall) => ServerAnswer),
      ) {
        routes.set(`${method} ${path}`, answer);
        return server;
      },
      to(method: string, path: string) {
        return server.calls.filter((call) => call.method === method && call.path === path);
      },
    },
  );

  return server;
}

/** A history and location that move together, starting at `url` (path, query and fragment). */
export function fakeNavigation(url: string) {
  let current = new URL(url, "https://panopticon.test");
  const set = (next: string) => {
    const url = new URL(next, current);
    if (url.origin !== current.origin) {
      // As the browser does: history can only move within the page's origin.
      throw new DOMException(`Cannot move history to ${url.origin}`, "SecurityError");
    }

    current = url;
  };
  const location = {
    get pathname() {
      return current.pathname;
    },
    get search() {
      return current.search;
    },
    get hash() {
      return current.hash;
    },
    get origin() {
      return current.origin;
    },
  };
  const history = {
    entries: [current.pathname + current.search] as string[],
    pushState(_data: unknown, _unused: string, next: string) {
      set(next);
      history.entries.push(current.pathname + current.search);
    },
    replaceState(_data: unknown, _unused: string, next: string) {
      set(next);
      history.entries[history.entries.length - 1] = current.pathname + current.search;
    },
  };
  return { location, history, path: () => current.pathname + current.search };
}

/** An in-memory Storage. */
export function memoryStorage(initial: Record<string, string> = {}) {
  const items = new Map(Object.entries(initial));
  return {
    items,
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  };
}

export const MEMBER = {
  id: "m1",
  email: "emmz@example.com",
  displayName: "Emmz",
  canRemoveMembers: true,
};

/** Waits until `check` passes (or fails after a few event-loop turns), for UI that updates after fetches. */
export async function until(check: () => boolean, turns = 20): Promise<void> {
  for (let i = 0; i < turns && !check(); i++) {
    await settle();
  }
}

/** The element matching `selector`; throws (failing the test) when there's none. */
export function q<T extends Element = HTMLElement>(root: ParentNode, selector: string): T {
  const found = root.querySelector<T>(selector);
  if (!found) {
    throw new Error(`No element matches ${selector}`);
  }

  return found;
}
