// Test harness for the whole signed-in app (imported by *.test.ts only).
import { RETURN_TO_KEY } from "./returnPath";
import { startShell, type StorageLike } from "./shell";
import {
  card,
  FakeEventSource,
  fakeNavigation,
  fakeServer,
  MEMBER,
  memoryStorage,
  type FakeServer,
} from "./testing";

export interface TestShellOptions {
  confirm?: (message: string) => boolean;
  copy?: (text: string) => Promise<void>;
  /** What localStorage holds at start. */
  local?: Record<string, string>;
  /** Replaces localStorage altogether, e.g. with one that throws, as blocked site data does. */
  storage?: StorageLike;
}

/** Starts the app at `url` against a fake server, with fake navigation, storage and EventSource. */
export async function startTestShell(
  url: string,
  server: FakeServer,
  options: TestShellOptions = {},
) {
  const root = document.createElement("main");
  const events = document.createElement("div");
  events.append(root);
  const nav = fakeNavigation(url);
  const local = memoryStorage(options.local);
  const shell = await startShell(root, {
    fetch: server,
    createEventSource: (eventsUrl) => new FakeEventSource(eventsUrl),
    local: options.storage ?? local,
    history: nav.history,
    location: nav.location,
    events,
    now: () => new Date("2026-11-08T12:00:00Z"),
    confirm: options.confirm ?? (() => true),
    copy: options.copy ?? (() => Promise.resolve()),
  });
  const heading = () => root.querySelector(".view h2")?.textContent;
  return { root, nav, local, shell, heading, events };
}

/** A server with a signed-in member and one card. */
export const signedIn = (member = MEMBER) =>
  fakeServer()
    .on("GET", "/api/session", { status: 200, body: member })
    .on("GET", "/api/changes", { status: 200, body: [card()] })
    .on("GET", "/api/settings", {
      status: 200,
      body: { idleAfterSeconds: 1800, staleAfterSeconds: 14400 },
    })
    .on("GET", "/api/history/filters", {
      status: 200,
      body: {
        repos: [],
        owners: [],
        members: [],
        machines: [],
        clients: [],
        projects: [],
        personal: false,
        tags: [],
      },
    });

/** A server with no session. */
export const signedOut = () => fakeServer().on("GET", "/api/session", { status: 401 });

/** The return path saved in `local`, or null (what the shell will go to after sign-in, before expiry). */
export function savedReturn(local: StorageLike): string | null {
  const raw = local.getItem(RETURN_TO_KEY);
  return raw ? (JSON.parse(raw) as { path: string }).path : null;
}
