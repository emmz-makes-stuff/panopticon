import { beforeEach, describe, expect, it, vi } from "vitest";
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
  project: null,
  personal: false,
  linkedTo: null,
  projectTags: [],
  personalTags: [],
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

  it("Tags shown on changes: a row's tags follow its meta line, and a row without tags has none", async () => {
    const PAN = { id: "p1", code: "PAN", name: "Panopticon" };
    const server = signedIn().on("GET", "/api/history", {
      status: 200,
      body: {
        changes: [
          {
            ...entry("ta", "team-auth", "2026-11-08T15:00:00Z"),
            project: PAN,
            projectTags: [{ id: "t1", key: "bug", value: null }],
            personalTags: [{ key: "dev", value: null }],
          },
          entry("ws", "walking-skeleton", "2026-11-07T10:00:00Z"),
        ],
        nextBefore: null,
      },
    });
    const { root } = await startTestShell("/history", server);
    await until(() => rows(root).length === 2);

    const [tagged, plain] = [...root.querySelectorAll<HTMLElement>(".history-item")];
    const tags = tagged?.querySelector(".tag-chips");
    expect(tags?.previousElementSibling?.classList.contains("history-meta")).toBe(true);
    expect(
      [...(tags?.querySelectorAll(".tag-chip") ?? [])].map((chip) => chip.textContent),
    ).toEqual(["PAN/bug", "dev (only you see this)"]);
    expect(plain?.querySelector(".tag-chips")).toBeNull();
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

  // Review fix 10.2: a failed first load can be retried without reloading the page.
  it("a failed first load offers to try again, which loads the page", async () => {
    let calls = 0;
    const server = signedIn().on("GET", "/api/history", () =>
      ++calls === 1
        ? { status: 500, body: { detail: "Database is down." } }
        : {
            status: 200,
            body: { changes: [entry("c1", "team-auth", "2026-11-08T20:11:15Z")], nextBefore: null },
          },
    );
    const { root } = await startTestShell("/history", server);
    await until(() => root.querySelector(".form-error")?.textContent !== "");
    const retry = q<HTMLButtonElement>(root, ".show-more");

    expect(retry.hidden).toBe(false);
    expect(retry.textContent).toBe("Try again");
    retry.click();
    await until(() => rows(root).length === 1);

    expect(rows(root)[0]?.change).toBe("team-auth");
    expect(retry.hidden).toBe(true);
    expect(q(root, ".form-error").textContent).toBe("");
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
    expect(links).toEqual(["Projects", "Members", "History", "Metrics", "Tokens"]);
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

const CHOICES = {
  repos: [
    { host: "github.com", owner: "acme", name: "site" },
    { host: "github.com", owner: "emmz", name: "panopticon" },
  ],
  owners: [
    { host: "github.com", owner: "acme" },
    { host: "github.com", owner: "emmz" },
  ],
  members: [{ id: "0190f5a2-0000-7000-8000-000000000001", displayName: "Emmz" }],
  machines: ["laptop", "vm-1"],
  clients: ["claude-code"],
  projects: [{ id: "6f1c2e4a-0000-4000-8000-000000000001", code: "PAN", name: "Panopticon" }],
  personal: true,
};

/** A history server that answers each page by the query it's asked with, and records the queries. */
function filteredHistory(
  pages: Record<string, { changes: HistoryEntry[]; nextBefore: string | null }>,
) {
  return signedIn()
    .on("GET", "/api/history/filters", { status: 200, body: CHOICES })
    .on("GET", "/api/history", (call) => {
      const query = call.url.split("?")[1] ?? "";
      return { status: 200, body: pages[query] ?? { changes: [], nextBefore: null } };
    });
}

const historyCalls = (server: ReturnType<typeof filteredHistory>) =>
  server.to("GET", "/api/history").map((call) => call.url);

// Spec change-filters: History filtered by the server, Filters in the address, Filter choices (history).
describe("history page filters", () => {
  const panopticon = "repo=github.com%2Femmz%2Fpanopticon";

  it("A filtered address: the first page is asked for with the filters, and the bar shows them", async () => {
    const server = filteredHistory({
      [panopticon]: {
        changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")],
        nextBefore: null,
      },
    });
    const { root } = await startTestShell(`/history?${panopticon}`, server);
    await until(() => rows(root).length === 1);

    expect(historyCalls(server)).toEqual([`/api/history?${panopticon}`]);
    expect(q<HTMLSelectElement>(root, "select[name=repo]").value).toBe(
      "github.com/emmz/panopticon",
    );
    expect(root.querySelector("select[name=status]")).toBeNull();
  });

  it("Show more keeps the filters", async () => {
    const cursor = "2026-11-08T15:00:00Z";
    const server = filteredHistory({
      [panopticon]: { changes: [entry("ta", "team-auth", cursor)], nextBefore: cursor },
      [`${panopticon}&before=${encodeURIComponent(cursor)}`]: {
        changes: [entry("ws", "walking-skeleton", "2026-11-07T10:00:00Z")],
        nextBefore: null,
      },
    });
    const { root } = await startTestShell(`/history?${panopticon}`, server);
    await until(() => rows(root).length === 1);

    q<HTMLButtonElement>(root, ".show-more").click();
    await until(() => rows(root).length === 2);

    expect(rows(root).map((r) => r.change)).toEqual(["team-auth", "walking-skeleton"]);
    expect(historyCalls(server)[1]).toBe(
      `/api/history?${panopticon}&before=${encodeURIComponent(cursor)}`,
    );
    expect(q<HTMLButtonElement>(root, ".show-more").hidden).toBe(true);
  });

  it("A filter change starts the list again in place, from the server, writing the address", async () => {
    const server = filteredHistory({
      "": { changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")], nextBefore: null },
      "machine=laptop": {
        changes: [entry("ws", "walking-skeleton", "2026-11-07T10:00:00Z")],
        nextBefore: null,
      },
    });
    const { root, nav } = await startTestShell("/history", server);
    await until(() => rows(root).length === 1);
    const heading = q(root, ".view h2");

    const select = q<HTMLSelectElement>(root, "select[name=machine]");
    select.value = "laptop";
    select.dispatchEvent(new Event("change"));
    await until(() => rows(root)[0]?.change === "walking-skeleton");

    expect(nav.path()).toBe("/history?machine=laptop");
    expect(rows(root).map((r) => r.change)).toEqual(["walking-skeleton"]);
    expect(historyCalls(server)).toEqual(["/api/history", "/api/history?machine=laptop"]);
    expect(q(root, ".view h2")).toBe(heading);
    expect(server.to("GET", "/api/history/filters")).toHaveLength(1);
  });

  it("A filtered history with no matches says so, with a way to clear the filters", async () => {
    const server = filteredHistory({});
    const { root, nav } = await startTestShell("/history?machine=nowhere", server);
    await until(() => root.querySelector(".no-match") !== null);

    expect(q(root, ".no-match h2").textContent).toBe("No archived changes match these filters");
    q<HTMLButtonElement>(root, ".no-match button").click();
    await until(() => nav.path() === "/history");

    expect(nav.path()).toBe("/history");
  });

  it("offers the server's choices, and a failed choices fetch still shows the list", async () => {
    const ok = filteredHistory({
      "": { changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")], nextBefore: null },
    });
    const shown = await startTestShell("/history", ok);
    await until(() => rows(shown.root).length === 1);
    expect(
      [...q<HTMLSelectElement>(shown.root, "select[name=owner]").options].map((o) => o.textContent),
    ).toEqual(["All owners", "github.com/acme", "github.com/emmz"]);

    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const failing = filteredHistory({
      "": { changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")], nextBefore: null },
    }).on("GET", "/api/history/filters", { status: 500 });
    const { root } = await startTestShell("/history", failing);
    await until(() => rows(root).length === 1);

    expect(rows(root).map((r) => r.change)).toEqual(["team-auth"]);
    expect(warn).toHaveBeenCalledWith(
      "Couldn't load the history's filter choices.",
      expect.anything(),
    );
    warn.mockRestore();
  });

  it("ignores a status in the address: every change here is archived", async () => {
    const server = filteredHistory({});
    await startTestShell("/history?status=stale&machine=vm-1", server);
    await until(() => historyCalls(server).length === 1);

    expect(historyCalls(server)).toEqual(["/api/history?machine=vm-1"]);
  });
});

// Projects D9: "History row shows its Project".
describe("history Project label", () => {
  it("History row shows its Project: the code plate, or Personal, leads the row's meta line", async () => {
    const server = signedIn().on("GET", "/api/history", {
      status: 200,
      body: {
        changes: [
          {
            ...entry("ta", "team-auth", "2026-11-08T15:00:00Z"),
            project: { id: "p1", code: "PAN", name: "Panopticon" },
          },
          { ...entry("ws", "walking-skeleton", "2026-11-07T10:00:00Z"), personal: true },
        ],
        nextBefore: null,
      },
    });
    const { root } = await startTestShell("/history", server);
    await until(() => rows(root).length === 2);

    expect(rows(root).map((row) => row.meta)).toEqual([
      "PAN github.com emmz/panopticon Archived 8 Nov 2026",
      "Personal github.com emmz/panopticon Archived 7 Nov 2026",
    ]);
    expect(q(root, ".history-item .project-code").title).toBe("Panopticon");
  });
});

// Spec change-filters (projects): "Filter by Project" and "Project filter choices" on the history.
describe("history Project filter", () => {
  const PAN_ID = "6f1c2e4a-0000-4000-8000-000000000001";

  it("offers the server's Projects by code and name, then Personal", async () => {
    const server = filteredHistory({
      "": { changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")], nextBefore: null },
    });
    const { root } = await startTestShell("/history", server);
    await until(() => rows(root).length === 1);

    expect(
      [...q<HTMLSelectElement>(root, "select[name=project]").options].map((o) => [
        o.value,
        o.textContent,
      ]),
    ).toEqual([
      ["", "All Projects"],
      [PAN_ID, "PAN: Panopticon"],
      ["personal", "Personal"],
    ]);
  });

  it("Personal only: choosing Personal asks the server for project=personal, writing the address", async () => {
    const server = filteredHistory({
      "": { changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")], nextBefore: null },
      "project=personal": {
        changes: [{ ...entry("mine", "cleanup", "2026-11-07T10:00:00Z"), personal: true }],
        nextBefore: null,
      },
    });
    const { root, nav } = await startTestShell("/history", server);
    await until(() => rows(root).length === 1);
    const select = q<HTMLSelectElement>(root, "select[name=project]");

    select.value = "personal";
    select.dispatchEvent(new Event("change"));
    await until(() => rows(root)[0]?.change === "cleanup");

    expect(historyCalls(server)).toEqual(["/api/history", "/api/history?project=personal"]);
    expect(nav.location.search).toBe("?project=personal");
  });

  it("One Project in the address: the first page is asked for with its id", async () => {
    const server = filteredHistory({
      [`project=${PAN_ID}`]: {
        changes: [entry("ta", "team-auth", "2026-11-08T15:00:00Z")],
        nextBefore: null,
      },
    });
    const { root } = await startTestShell(`/history?project=${PAN_ID}`, server);
    await until(() => rows(root).length === 1);

    expect(historyCalls(server)).toEqual([`/api/history?project=${PAN_ID}`]);
    expect(q<HTMLSelectElement>(root, "select[name=project]").value).toBe(PAN_ID);
  });
});

// Spec change-history (projects): "A copy retired by linking".
describe("history retired copy", () => {
  it("A copy retired by linking: listed as Linked to PAN, with no archive folder", async () => {
    const server = signedIn().on("GET", "/api/history", {
      status: 200,
      body: {
        changes: [
          {
            ...entry("mine", "cleanup", "2026-11-08T15:00:00Z"),
            archiveFolder: null,
            personal: true,
            linkedTo: { id: "p1", code: "PAN", name: "Panopticon" },
          },
          entry("ws", "walking-skeleton", "2026-11-07T10:00:00Z"),
        ],
        nextBefore: null,
      },
    });
    const { root } = await startTestShell("/history", server);
    await until(() => rows(root).length === 2);

    expect(rows(root).map((row) => row.meta)).toEqual([
      "Personal github.com emmz/panopticon Linked to PAN on 8 Nov 2026",
      "github.com emmz/panopticon Archived 7 Nov 2026",
    ]);
  });
});
