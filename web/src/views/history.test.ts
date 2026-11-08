import { beforeEach, describe, expect, it } from "vitest";
import { signedIn, signedOut, savedReturn, startTestShell } from "../testShell";
import { card, FakeEventSource, q, until } from "../testing";
import type { HistoryEntry } from "../types";

const entry = (changeId: string, change: string, archivedAt: string): HistoryEntry => ({
  changeId,
  repo: { host: "github.com", owner: "emmz", name: "panopticon" },
  change,
  archivedAt,
  archiveFolder: `${archivedAt.slice(0, 10)}-${change}`,
  agents: [{ client: "claude-code", machine: "vm-1", lastReportedAt: archivedAt, member: "Emmz" }],
});

const rows = (root: HTMLElement) =>
  [...root.querySelectorAll<HTMLElement>(".history-item")].map((item) => ({
    change: item.querySelector(".history-change")?.textContent,
    href: item.querySelector("a")?.getAttribute("href"),
    meta: item.querySelector(".history-meta")?.textContent,
    agents: item.querySelector(".history-agents")?.textContent,
  }));

const click = (link: Element | null | undefined) =>
  link?.dispatchEvent(new MouseEvent("click", { bubbles: true, button: 0 }));

beforeEach(() => {
  FakeEventSource.instances.length = 0;
});

// Spec change-history: History list, Final snapshot (the link to it).
describe("history page", () => {
  it("Two archived changes: newest archive first, each with its repo, archive date and agents, linking to its page", async () => {
    const server = signedIn().on("GET", "/api/history", {
      status: 200,
      body: {
        changes: [
          entry("ta", "team-auth", "2026-11-08T15:00:00Z"),
          entry("ws", "walking-skeleton", "2026-11-07T10:00:00Z"),
        ],
        nextBefore: null,
      },
    });
    const { root, heading } = await startTestShell("/history", server);
    await until(() => rows(root).length === 2);

    expect(heading()).toBe("History");
    expect(rows(root)).toEqual([
      {
        change: "team-auth",
        href: "/changes/ta",
        meta: "github.com emmz/panopticon Archived 8 Nov 2026",
        agents: "claude-code@vm-1 Emmz",
      },
      {
        change: "walking-skeleton",
        href: "/changes/ws",
        meta: "github.com emmz/panopticon Archived 7 Nov 2026",
        agents: "claude-code@vm-1 Emmz",
      },
    ]);
    expect(q(root, ".show-more").hidden).toBe(true);
  });

  it("Nothing archived yet: an empty state saying archived changes will appear here", async () => {
    const server = signedIn().on("GET", "/api/history", {
      status: 200,
      body: { changes: [], nextBefore: null },
    });
    const { root } = await startTestShell("/history", server);
    await until(() => root.querySelector(".history-empty") !== null);

    expect(q(root, ".history-empty").textContent).toContain("appears here");
    expect(q(root, ".show-more").hidden).toBe(true);
  });

  it("Show more fetches the next page by its cursor and appends it", async () => {
    const server = signedIn().on("GET", "/api/history", (call) => ({
      status: 200,
      body: call.url.includes("before=")
        ? { changes: [entry("b", "second", "2026-11-07T15:00:00Z")], nextBefore: null }
        : {
            changes: [entry("a", "first", "2026-11-08T15:00:00Z")],
            nextBefore: "2026-11-08T15:00:00Z",
          },
    }));
    const { root } = await startTestShell("/history", server);
    await until(() => rows(root).length === 1);

    const more = q<HTMLButtonElement>(root, ".show-more");
    expect(more.hidden).toBe(false);
    more.click();
    await until(() => rows(root).length === 2);

    expect(rows(root).map((r) => r.change)).toEqual(["first", "second"]);
    expect(server.to("GET", "/api/history").map((call) => call.url)).toEqual([
      "/api/history",
      "/api/history?before=2026-11-08T15%3A00%3A00Z",
    ]);
    expect(more.hidden).toBe(true);
  });

  it("a failed load says so", async () => {
    const server = signedIn().on("GET", "/api/history", {
      status: 500,
      body: { detail: "Database is down." },
    });
    const { root } = await startTestShell("/history", server);
    await until(() => root.querySelector(".form-error")?.textContent !== "");

    expect(q(root, ".form-error").textContent).toBe("Database is down.");
  });

  it("Signed out: returns to the history after sign-in", async () => {
    const { nav, local } = await startTestShell("/history", signedOut());

    expect(nav.path()).toBe("/sign-in");
    expect(savedReturn(local)).toBe("/history");
  });
});

// Spec live-dashboard: History link, Card opens its change.
describe("history link and card links", () => {
  it("Go to history: the header's History link opens the history page", async () => {
    const server = signedIn().on("GET", "/api/history", {
      status: 200,
      body: { changes: [], nextBefore: null },
    });
    const { root, nav, heading } = await startTestShell("/", server);

    const links = [...root.querySelectorAll<HTMLAnchorElement>("nav.account a")].map(
      (a) => a.textContent,
    );
    expect(links).toEqual(["Members", "History", "Tokens"]);
    click([...root.querySelectorAll("nav.account a")].find((a) => a.textContent === "History"));
    await until(() => heading() === "History");

    expect(nav.path()).toBe("/history");
  });

  it("Open from a card: the change name is an ordinary link to the change page", async () => {
    const server = signedIn().on("GET", "/api/changes/c1", {
      status: 200,
      body: {
        card: card({ change: "team-auth" }),
        archivedAt: null,
        archiveFolder: null,
        documents: [],
      },
    });
    const { root, nav, heading, events } = await startTestShell("/", server);
    await until(() => root.querySelector("article.card") !== null);

    const link = q<HTMLAnchorElement>(root, "article.card .card-change a");
    expect(link.getAttribute("href")).toBe("/changes/c1");
    expect(link.textContent).toBe("walking-skeleton");

    // A real link, so the keyboard reaches it and Enter follows it, and a modified click opens a new tab.
    document.body.append(events);
    link.focus();
    expect(document.activeElement).toBe(link);
    expect(link.dataset["route"]).toBe("");
    const modified = new MouseEvent("click", {
      bubbles: true,
      cancelable: true,
      button: 0,
      metaKey: true,
    });
    link.dispatchEvent(modified);
    expect(modified.defaultPrevented).toBe(false);
    expect(nav.path()).toBe("/");

    click(link);
    await until(() => heading() === "team-auth");
    expect(nav.path()).toBe("/changes/c1");
    events.remove();
  });
});
