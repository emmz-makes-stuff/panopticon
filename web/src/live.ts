import type { App } from "./app";
import type { ChangeCard } from "./types";

/** The SSE event the server sends for every changed card (server: Dashboard/DashboardEndpoints). */
export const CHANGE_UPDATED = "change-updated";

/** The parts of the browser's EventSource this module uses, so tests can supply a fake. */
export interface EventSourceLike {
  readonly readyState: number;
  onopen: ((event: Event) => void) | null;
  onerror: ((event: Event) => void) | null;
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void;
  close(): void;
}

export type EventSourceFactory = (url: string) => EventSourceLike;

export interface LiveOptions {
  createEventSource?: EventSourceFactory | undefined;
  /** Delays before reopening after the browser gives up on a connection; the last one repeats. */
  retryDelaysMs?: readonly number[] | undefined;
  setTimer?: ((callback: () => void, ms: number) => unknown) | undefined;
  /**
   * Asked after a stream error, to tell "signed out" from "offline" (design D18): resolves false when the session
   * has ended. The stream then stops for good, with no reconnect. A check that fails counts as still signed in.
   */
  checkSignedIn?: (() => Promise<boolean>) | undefined;
}

/** EventSource.CLOSED: the browser won't retry by itself. */
const CLOSED = 2;

/**
 * Keeps the dashboard live (spec live-dashboard, "Live updates", and design D9). On every (re)connect it refreshes
 * all cards from `GET /api/changes`, then applies `change-updated` events in place, ignoring stale ones. While
 * disconnected it shows the offline state. The browser retries a dropped stream by itself; when it gives up (the
 * stream is closed) this reopens it after a back-off. Returns a function that stops it.
 */
export function connectLive(app: App, options: LiveOptions = {}): () => void {
  const create = options.createEventSource ?? ((url) => new EventSource(url));
  const delays = options.retryDelaysMs ?? [1000, 2000, 5000, 10000];
  const setTimer = options.setTimer ?? ((callback, ms) => setTimeout(callback, ms));

  let source: EventSourceLike | null = null;
  let failures = 0;
  let stopped = false;

  function open(): void {
    const current = create("/api/events");
    source = current;

    current.onopen = () => {
      // Events can be missed while disconnected, so start from the server's full state.
      app.refresh().then(
        () => {
          failures = 0;
          app.setStatus("live");
        },
        () => app.setStatus("offline"),
      );
    };

    current.addEventListener(CHANGE_UPDATED, (event) => {
      const card = parseCard(event.data);
      if (card) {
        app.upsert(card);
      }
    });

    current.onerror = () => {
      app.setStatus("offline");
      if (options.checkSignedIn) {
        // The browser doesn't say why a stream failed: a 401 (the session ended) looks like a dropped connection.
        void options.checkSignedIn().then(
          (signedIn) => (signedIn ? retry() : stopFor(current)),
          () => retry(),
        );
        return;
      }

      retry();
    };

    function stopFor(failed: EventSourceLike): void {
      stopped = true;
      failed.close();
    }

    function retry(): void {
      if (current.readyState === CLOSED && !stopped) {
        current.close();
        const delay = delays[Math.min(failures, delays.length - 1)] ?? 1000;
        failures++;
        setTimer(() => {
          if (!stopped) {
            open();
          }
        }, delay);
      }
    }
  }

  open();

  return () => {
    stopped = true;
    source?.close();
  };
}

function parseCard(data: string): ChangeCard | null {
  try {
    const card = JSON.parse(data) as Partial<ChangeCard> | null;
    return card && typeof card.changeId === "string" && typeof card.lastReportedAt === "string"
      ? (card as ChangeCard)
      : null;
  } catch {
    return null;
  }
}
