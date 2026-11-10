import { beforeEach, describe, expect, it } from "vitest";
import { mountApp } from "./app";
import { CHANGE_ARCHIVED, CHANGE_UPDATED, connectLive, PROJECTS_CHANGED } from "./live";
import { card, FakeEventSource, fakeFetch, NOW, settle } from "./testing";
import type { ChangeCard } from "./types";

const later = (minutes: number) =>
  new Date(Date.parse("2026-11-08T11:57:00Z") + minutes * 60_000).toISOString();

async function start(initial: ChangeCard[] = [card()]) {
  const root = document.createElement("main");
  const fetch = fakeFetch(initial);
  const app = await mountApp(root, { fetch, now: () => NOW });
  const timers: { callback: () => void; ms: number }[] = [];
  const stop = connectLive(app, {
    createEventSource: (url) => new FakeEventSource(url),
    setTimer: (callback, ms) => timers.push({ callback, ms }),
  });

  const cards = () => [...root.querySelectorAll<HTMLElement>("article.card")];
  const status = () => root.querySelector(".live")?.textContent;
  const sectionCount = (element: Element | undefined) =>
    element?.querySelector(".bar-section .bar-count")?.textContent;
  return { root, fetch, app, timers, stop, cards, status, sectionCount };
}

const send = (update: ChangeCard) =>
  FakeEventSource.latest().emit(CHANGE_UPDATED, JSON.stringify(update));

beforeEach(() => {
  FakeEventSource.instances.length = 0;
});

// Spec live-dashboard, "Live updates".
describe("connectLive", () => {
  it("connects to /api/events, refreshes on open and shows that it is live", async () => {
    const { fetch, status } = await start();
    expect(status()).toBe("Connecting…");

    FakeEventSource.latest().open();
    await settle();

    expect(FakeEventSource.latest().url).toBe("/api/events");
    expect(fetch.urls).toEqual(["/api/changes", "/api/changes"]);
    expect(status()).toBe("Live");
  });

  it("Report moves a bar: updates the card in place, with no reload, lighting the new cell", async () => {
    const { fetch, cards, sectionCount } = await start();
    FakeEventSource.latest().open();
    await settle();
    const fetchesBefore = fetch.urls.length;

    send(
      card({
        lastReportedAt: later(1),
        currentSection: { number: 2, title: "Core", progress: { done: 3, total: 5 } },
        overall: { done: 8, total: 20 },
      }),
    );

    expect(cards()).toHaveLength(1);
    expect(sectionCount(cards()[0])).toBe("3/5 (60%)");
    expect(cards()[0]?.querySelector(".bar-overall .bar-count")?.textContent).toBe("8/20 (40%)");
    expect(fetch.urls).toHaveLength(fetchesBefore);

    const lighting = [...(cards()[0]?.querySelectorAll(".bar-section .cell.lighting") ?? [])];
    expect(lighting).toHaveLength(1);
    expect(cards()[0]?.classList.contains("updated")).toBe(true);
  });

  it("New change appears: a first report adds a card at the top", async () => {
    const { cards } = await start();
    FakeEventSource.latest().open();
    await settle();

    send(card({ changeId: "new", change: "add-auth", lastReportedAt: later(5) }));

    expect(cards().map((c) => c.querySelector(".card-change")?.textContent)).toEqual([
      "add-auth",
      "walking-skeleton",
    ]);
  });

  it("moves an updated card to the top, keeping most recent first", async () => {
    const { cards } = await start([
      card({ changeId: "b", change: "newer", lastReportedAt: later(2) }),
      card({ changeId: "a", change: "older" }),
    ]);

    send(card({ changeId: "a", change: "older", lastReportedAt: later(3) }));

    expect(cards().map((c) => c.dataset["changeId"])).toEqual(["a", "b"]);
  });

  it("ignores stale and duplicate events", async () => {
    const { cards, sectionCount } = await start([card({ lastReportedAt: later(10) })]);

    send(
      card({
        lastReportedAt: later(5),
        currentSection: { number: 2, title: "Core", progress: { done: 5, total: 5 } },
      }),
    );
    send(
      card({
        lastReportedAt: later(10),
        currentSection: { number: 2, title: "Core", progress: { done: 4, total: 5 } },
      }),
    );

    expect(sectionCount(cards()[0])).toBe("2/5 (40%)");
  });

  it("ignores events whose data isn't a card", async () => {
    const { cards } = await start();

    FakeEventSource.latest().emit(CHANGE_UPDATED, "not json");
    FakeEventSource.latest().emit(CHANGE_UPDATED, '{"hello":1}');

    expect(cards()).toHaveLength(1);
  });

  it("Connection drops and returns: shows offline, then refreshes every card on reconnect", async () => {
    const { fetch, cards, status, sectionCount } = await start();
    const source = FakeEventSource.latest();
    source.open();
    await settle();

    source.fail(0); // The browser retries by itself.
    expect(status()).toBe("Offline, reconnecting…");
    expect(FakeEventSource.instances).toHaveLength(1);

    // While offline, the server moved on.
    fetch.cards = [
      card({ changeId: "other", change: "add-auth", lastReportedAt: later(20) }),
      card({
        lastReportedAt: later(15),
        currentSection: { number: 2, title: "Core", progress: { done: 4, total: 5 } },
      }),
    ];
    source.open();
    await settle();

    expect(status()).toBe("Live");
    expect(cards().map((c) => c.dataset["changeId"])).toEqual(["other", "c1"]);
    expect(sectionCount(cards()[1])).toBe("4/5 (80%)");
  });

  it("reopens the stream after a back-off when the browser gives up", async () => {
    const { timers, status } = await start();
    const first = FakeEventSource.latest();
    first.open();
    await settle();

    first.fail(2);

    expect(first.closed).toBe(true);
    expect(status()).toBe("Offline, reconnecting…");
    expect(timers.map((t) => t.ms)).toEqual([1000]);

    timers[0]?.callback();
    const second = FakeEventSource.latest();
    expect(second).not.toBe(first);

    second.fail(2);
    expect(timers.map((t) => t.ms)).toEqual([1000, 2000]);

    timers[1]?.callback();
    FakeEventSource.latest().open();
    await settle();
    expect(status()).toBe("Live");

    // A successful connection resets the back-off.
    FakeEventSource.latest().fail(2);
    expect(timers.map((t) => t.ms)).toEqual([1000, 2000, 1000]);
  });

  it("stays offline if the refresh after reconnecting fails", async () => {
    const { fetch, status } = await start();
    fetch.status = 503;

    FakeEventSource.latest().open();
    await settle();

    expect(status()).toBe("Offline, reconnecting…");
  });

  it("stops: closes the stream and doesn't reopen it", async () => {
    const { stop, timers } = await start();
    const source = FakeEventSource.latest();

    stop();
    source.fail(2);

    expect(source.closed).toBe(true);
    expect(timers).toHaveLength(0);
  });
});

describe("App.render after a reconnect", () => {
  it("keeps a card that an event made newer than the loaded list", async () => {
    const root = document.createElement("main");
    const app = await mountApp(root, { fetch: fakeFetch([]), now: () => NOW });
    app.upsert(card({ lastReportedAt: later(10), overall: { done: 9, total: 20 } }));

    app.render([card({ lastReportedAt: later(5) })]);

    expect(root.querySelector(".bar-overall .bar-count")?.textContent).toBe("9/20 (45%)");
  });
});

// change-documents-and-history: live-dashboard "Archived card leaves".
describe("connectLive, archived changes", () => {
  it("Archive while watching: the archived change's card leaves, with no reload", async () => {
    const { fetch, cards } = await start([
      card({ changeId: "team-auth", change: "team-auth" }),
      card({ changeId: "other", change: "other", lastReportedAt: later(-1) }),
    ]);
    FakeEventSource.latest().open();
    await settle();
    const fetchesBefore = fetch.urls.length;

    FakeEventSource.latest().emit(CHANGE_ARCHIVED, JSON.stringify({ changeId: "team-auth" }));

    expect(cards().map((c) => c.querySelector(".card-change")?.textContent)).toEqual(["other"]);
    expect(fetch.urls).toHaveLength(fetchesBefore);
  });

  it("shows the empty state once the last card leaves, and ignores unknown or malformed events", async () => {
    const { root, cards } = await start([card({ changeId: "team-auth" })]);
    FakeEventSource.latest().open();
    await settle();

    FakeEventSource.latest().emit(CHANGE_ARCHIVED, JSON.stringify({ changeId: "someone-else" }));
    FakeEventSource.latest().emit(CHANGE_ARCHIVED, "not json");
    expect(cards()).toHaveLength(1);

    FakeEventSource.latest().emit(CHANGE_ARCHIVED, JSON.stringify({ changeId: "team-auth" }));
    expect(cards()).toHaveLength(0);
    expect(root.querySelector(".empty-state")).not.toBeNull();
  });
});

// Spec change-visibility, "Membership changes take effect live" (projects D3, §11).
describe("connectLive, membership changes", () => {
  it("reconnects when the refetch after joining a Project fails, so the reconnect's refresh catches up", async () => {
    const { fetch, timers, status } = await start();
    FakeEventSource.latest().open();
    await settle();

    fetch.status = 500;
    FakeEventSource.latest().emit(
      PROJECTS_CHANGED,
      JSON.stringify({ projectId: "pan", member: true }),
    );
    await settle();

    expect(status()).toBe("Offline, reconnecting…");
    expect(FakeEventSource.latest().closed).toBe(true);
    expect(timers).toHaveLength(1);
    timers[0]?.callback();
    expect(FakeEventSource.instances).toHaveLength(2);
  });
});
