import { beforeEach, describe, expect, it, vi } from "vitest";
import { RETURN_TO_KEY } from "./shell";
import { FakeEventSource, fakeServer, MEMBER, settle, until, q } from "./testing";
import { signedIn, startTestShell } from "./testShell";

beforeEach(() => {
  FakeEventSource.instances.length = 0;
});

// Design D18; spec dashboard-sign-in, "Dashboard requires sign-in".
describe("startShell", () => {
  it("Not signed in: shows the sign-in page, loads no change data, and remembers where you were going", async () => {
    const server = fakeServer().on("GET", "/api/session", { status: 401 });

    const { heading, nav, session, root } = await startTestShell("/", server);

    expect(heading()).toBe("Sign in");
    expect(nav.path()).toBe("/sign-in");
    expect(server.to("GET", "/api/changes")).toHaveLength(0);
    expect(FakeEventSource.instances).toHaveLength(0);
    expect(session.getItem(RETURN_TO_KEY)).toBe("/");
    expect(root.querySelector(".account")?.childElementCount).toBe(0);
  });

  it("asks for the session before anything else, then shows the cards and opens the stream", async () => {
    const server = signedIn();

    const { root } = await startTestShell("/", server);

    expect(server.calls[0]?.path).toBe("/api/session");
    expect(root.querySelectorAll("article.card")).toHaveLength(1);
    expect(FakeEventSource.latest().url).toBe("/api/events");
  });

  it("shows the signed-in header: your name as text, Members, Tokens and Sign out", async () => {
    const { root } = await startTestShell(
      "/",
      signedIn({ ...MEMBER, displayName: "<img src=x onerror=alert(1)>" }),
    );

    const account = q(root, ".account");
    expect(account.querySelector(".account-name")?.textContent).toBe(
      "<img src=x onerror=alert(1)>",
    );
    expect(account.querySelector("img")).toBeNull();
    expect(
      [...account.querySelectorAll("a")].map((a) => [a.textContent, a.getAttribute("href")]),
    ).toEqual([
      ["Members", "/members"],
      ["Tokens", "/tokens"],
    ]);
    expect(account.querySelector("button")?.textContent).toBe("Sign out");
  });

  it("Session ends while watching: a stream error with a 401 session check stops updates and shows sign-in", async () => {
    const server = signedIn();
    const { heading, nav, session } = await startTestShell("/", server);
    const stream = FakeEventSource.latest();
    stream.open();
    await settle();
    server.on("GET", "/api/session", { status: 401 });

    // A 401 on the stream's reconnect: the browser reports only an error, and gives up.
    stream.fail(2);
    await until(() => heading() === "Sign in");

    expect(heading()).toBe("Sign in");
    expect(nav.path()).toBe("/sign-in");
    expect(stream.closed).toBe(true);
    await settle();
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(session.getItem(RETURN_TO_KEY)).toBe("/");
  });

  it("keeps reconnecting when the stream drops but the session is still good (offline, not signed out)", async () => {
    vi.useFakeTimers();
    try {
      const { root } = await startTestShell("/", signedIn());
      const stream = FakeEventSource.latest();

      stream.fail(2);
      await vi.advanceTimersByTimeAsync(1000);

      expect(root.querySelector(".live")?.textContent).toBe("Offline, reconnecting…");
      expect(FakeEventSource.instances).toHaveLength(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it("any 401 while signed in sends you to sign in, remembering the page", async () => {
    const server = signedIn().on("GET", "/api/members", { status: 401 });

    const { heading, nav, session } = await startTestShell("/members", server);
    await until(() => heading() === "Sign in");

    expect(nav.path()).toBe("/sign-in");
    expect(session.getItem(RETURN_TO_KEY)).toBe("/members");
  });

  it("Sign out ends the session on the server and shows the sign-in page", async () => {
    const server = signedIn().on("POST", "/api/auth/sign-out", { status: 204 });
    const { root, heading, session } = await startTestShell("/", server);

    q<HTMLButtonElement>(root, ".account button").click();
    await until(() => heading() === "Sign in");

    expect(server.to("POST", "/api/auth/sign-out")).toHaveLength(1);
    expect(heading()).toBe("Sign in");
    expect(root.querySelector(".account")?.childElementCount).toBe(0);
    expect(session.getItem(RETURN_TO_KEY)).toBeNull();
    expect(FakeEventSource.latest().closed).toBe(true);
  });

  it("says Sign out failed, and stays signed in, when the server can't be reached", async () => {
    const server = signedIn().on("POST", "/api/auth/sign-out", { status: 503 });
    const { root, heading } = await startTestShell("/", server);
    const before = heading();

    q<HTMLButtonElement>(root, ".account button").click();
    await until(() => root.querySelector(".account-error")?.textContent !== "");

    expect(q(root, ".account-error").textContent).toBe("Couldn't sign out. Try again.");
    expect(q(root, ".account-name").textContent).toBe("Emmz");
    expect(heading()).toBe(before);
  });

  it("says the server can't be reached when the session check fails without a 401, and starts no view", async () => {
    const server = fakeServer().on("GET", "/api/session", { status: 503 });
    const { root, shell } = await startTestShell("/", server);

    expect(q(root, ".view [role=alert] h2").textContent).toBe("Couldn't reach the server");
    expect(shell.router).toBeNull();
    expect(server.to("GET", "/api/changes")).toHaveLength(0);
  });

  it("sends a signed-in member away from /sign-in to the cards", async () => {
    const { nav } = await startTestShell("/sign-in", signedIn());

    expect(nav.path()).toBe("/");
  });
});
