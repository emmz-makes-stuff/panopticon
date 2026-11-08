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
    expect(text(root, ".empty-state pre")).toContain("PANOPTICON_SERVER_URL");
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
