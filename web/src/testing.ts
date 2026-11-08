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
