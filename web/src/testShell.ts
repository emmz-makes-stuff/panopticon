// Test harness for the whole signed-in app (imported by *.test.ts only).
import { startShell } from "./shell";
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
  /** What localStorage holds at start. */
  local?: Record<string, string>;
  /** What sessionStorage holds at start. */
  session?: Record<string, string>;
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
  const session = memoryStorage(options.session);
  const local = memoryStorage(options.local);
  const shell = await startShell(root, {
    fetch: server,
    createEventSource: (eventsUrl) => new FakeEventSource(eventsUrl),
    session,
    local,
    history: nav.history,
    location: nav.location,
    events,
    now: () => new Date("2026-11-08T12:00:00Z"),
    confirm: options.confirm ?? (() => true),
  });
  const heading = () => root.querySelector(".view h2")?.textContent;
  return { root, nav, session, local, shell, heading, events };
}

/** A server with a signed-in member and one card. */
export const signedIn = (member = MEMBER) =>
  fakeServer()
    .on("GET", "/api/session", { status: 200, body: member })
    .on("GET", "/api/changes", { status: 200, body: [card()] });

/** A server with no session. */
export const signedOut = () => fakeServer().on("GET", "/api/session", { status: 401 });
