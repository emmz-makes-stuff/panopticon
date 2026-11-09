import { describe, expect, it } from "vitest";
import { mountApp, PRODUCT_NAME } from "./app";
import { card, fakeFetch, NOW } from "./testing";

async function mount(cards = [card()], status = 200) {
  const root = document.createElement("main");
  const fetch = fakeFetch(cards, status);
  const app = await mountApp(root, { fetch, now: () => NOW });
  return { root, fetch, app };
}

const cardsIn = (root: HTMLElement) => [...root.querySelectorAll<HTMLElement>("article.card")];
const text = (root: ParentNode, selector: string) => root.querySelector(selector)?.textContent;

describe("mountApp", () => {
  it("names the product exactly", async () => {
    const { root } = await mount();

    expect(text(root, "h1")).toBe(PRODUCT_NAME);
    expect(PRODUCT_NAME).toBe("a PANOPTICON");
  });

  // Spec live-dashboard, "One card per change".
  it("shows one card per change, in the server's most-recent-first order", async () => {
    const { root, fetch } = await mount([
      card({
        changeId: "b",
        change: "add-auth",
        repo: { host: "github.com", owner: "emmz", name: "other" },
      }),
      card({ changeId: "a", change: "walking-skeleton" }),
    ]);

    expect(fetch.urls).toEqual(["/api/changes"]);
    const cards = cardsIn(root);
    expect(cards.map((c) => text(c, ".card-change"))).toEqual(["add-auth", "walking-skeleton"]);
    expect(cards.map((c) => c.dataset["changeId"])).toEqual(["b", "a"]);
    expect(text(root, ".change-count")).toBe("2 changes");
  });

  it("shows an empty state explaining how to connect an agent", async () => {
    const { root } = await mount([]);

    expect(cardsIn(root)).toHaveLength(0);
    expect(text(root, ".empty-state h2")).toBe("No agents have reported yet");
    expect(text(root, ".empty-state pre")).toContain("panopticon mcp");
    expect(text(root, ".empty-state pre")).toContain(
      `panopticon login --server ${location.origin}`,
    );
  });

  it("says when the changes couldn't be loaded", async () => {
    const { root } = await mount([], 500);

    expect(root.querySelector("[role=alert]")?.textContent).toContain(
      "GET /api/changes answered 500",
    );
  });

  it("keeps the last-reported times current as the clock moves on", async () => {
    const root = document.createElement("main");
    let clock = NOW;
    const app = await mountApp(root, { fetch: fakeFetch([card()]), now: () => clock });
    expect(text(root, "time")).toBe("3m ago");

    clock = new Date(NOW.getTime() + 2 * 3600_000);
    app.updateTimes();

    expect(text(root, "time")).toBe("2h ago");
  });

  it("re-renders when given new cards", async () => {
    const { root, app } = await mount();

    app.render([card({ changeId: "x", change: "next" }), card()]);

    expect(cardsIn(root)).toHaveLength(2);
    expect(text(root, ".change-count")).toBe("2 changes");
  });
});

const minutesAgo = (minutes: number) => new Date(NOW.getTime() - minutes * 60_000).toISOString();

// Spec live-dashboard, "Quiet cards" and "Quiet state moves on by itself" (design D2), with the default thresholds.
describe("mountApp quiet cards", () => {
  const stateOf = (root: HTMLElement) => {
    const element = root.querySelector<HTMLElement>("article.card");
    return {
      quiet: element?.dataset["quiet"],
      idle: element?.classList.contains("card--idle"),
      stale: element?.classList.contains("card--stale"),
      label: element?.querySelector<HTMLElement>(".card-state"),
      time: text(root, ".card-time"),
    };
  };

  it("Recently reported: a card reported 5 minutes ago is active, with no state label", async () => {
    const { root } = await mount([card({ lastReportedAt: minutesAgo(5) })]);

    const state = stateOf(root);
    expect(state).toMatchObject({ quiet: "active", idle: false, stale: false, time: "5m ago" });
    expect(state.label?.hidden).toBe(true);
  });

  it("Quiet for an hour: the card is shown as idle, named in text", async () => {
    const { root } = await mount([card({ lastReportedAt: minutesAgo(60) })]);

    const state = stateOf(root);
    expect(state).toMatchObject({ quiet: "idle", idle: true, stale: false, time: "1h ago" });
    expect(state.label?.hidden).toBe(false);
    expect(state.label?.textContent).toBe("Idle");
  });

  it("Quiet for a morning: the card is stale, saying when it was last seen", async () => {
    const { root } = await mount([card({ lastReportedAt: minutesAgo(300) })]);

    const state = stateOf(root);
    expect(state).toMatchObject({
      quiet: "stale",
      idle: false,
      stale: true,
      time: "last seen 5h ago",
    });
    expect(state.label?.textContent).toBe("Stale");
    expect(text(root, ".card-when")).toBe("Stale · last seen 5h ago");
  });

  it("Goes idle while watching: one tick after 30 minutes pass, with no report or reload", async () => {
    const root = document.createElement("main");
    let clock = NOW;
    const fetch = fakeFetch([card({ lastReportedAt: minutesAgo(25) })]);
    const app = await mountApp(root, { fetch, now: () => clock });
    expect(stateOf(root).quiet).toBe("active");

    clock = new Date(NOW.getTime() + 6 * 60_000);
    app.updateTimes();

    expect(stateOf(root)).toMatchObject({ quiet: "idle", time: "31m ago" });
    expect(fetch.urls).toEqual(["/api/changes"]);
  });

  it("Report wakes a stale card: it's active as soon as the update arrives", async () => {
    const { root, app } = await mount([card({ lastReportedAt: minutesAgo(300) })]);
    expect(stateOf(root).quiet).toBe("stale");

    app.upsert(card({ lastReportedAt: NOW.toISOString() }));

    expect(stateOf(root)).toMatchObject({ quiet: "active", stale: false, time: "just now" });
  });

  it("uses the deployment's thresholds when given them", async () => {
    const root = document.createElement("main");
    await mountApp(root, {
      fetch: fakeFetch([card({ lastReportedAt: minutesAgo(3) })]),
      now: () => NOW,
      quiet: { idleAfterSeconds: 60, staleAfterSeconds: 120 },
    });

    expect(stateOf(root).quiet).toBe("stale");
  });
});
