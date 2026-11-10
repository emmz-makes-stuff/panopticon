import { describe, expect, it } from "vitest";
import { mountApp, PRODUCT_NAME } from "./app";
import { parseFilters } from "./filters";
import { card, fakeFetch, NOW, q } from "./testing";
import type { ChangeCard } from "./types";

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
    expect(text(root, ".card-when")).toBe("Stale, last seen 5h ago");
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

const EMMZ_ID = "0190f5a2-0000-7000-8000-000000000001";
const repoCard = (changeId: string, name: string, reportedAt = minutesAgo(3)) =>
  card({
    changeId,
    change: name,
    repo: { host: "github.com", owner: "emmz", name },
    lastReportedAt: reportedAt,
  });
const filtered = (query: string) => parseFilters(new URLSearchParams(query));

// Spec change-filters: Live updates respect filters, Filter by status, Filters combine, Filter choices (dashboard).
describe("mountApp filters", () => {
  async function mountFiltered(cards: ChangeCard[], query: string, clock = () => NOW) {
    const root = document.createElement("main");
    const app = await mountApp(root, {
      fetch: fakeFetch(cards),
      now: clock,
      filters: filtered(query),
    });
    return { root, app };
  }

  const names = (root: HTMLElement) => cardsIn(root).map((c) => text(c, ".card-change"));

  it("Report outside the filter: no card appears, and the count says one more is hidden", async () => {
    const { root, app } = await mountFiltered(
      [repoCard("a", "panopticon")],
      "repo=github.com/emmz/panopticon",
    );

    app.upsert(repoCard("b", "other", NOW.toISOString()));

    expect(names(root)).toEqual(["panopticon"]);
    expect(text(root, ".change-count")).toBe("1 of 2 changes");
    expect(root.querySelector(".change-count")?.getAttribute("title")).toBe(
      "1 hidden by the filters",
    );
  });

  it("A new agent joins a change: once an agent on the filtered machine reports, the card appears", async () => {
    const before = card({ lastReportedAt: minutesAgo(10) });
    const { root, app } = await mountFiltered([before], "machine=vm-2");
    expect(names(root)).toEqual([]);

    app.upsert({
      ...before,
      lastReportedAt: NOW.toISOString(),
      agents: [
        ...before.agents,
        { client: "claude-code", machine: "vm-2", lastReportedAt: NOW.toISOString() },
      ],
    });

    expect(names(root)).toEqual(["walking-skeleton"]);
  });

  it("Only stale cards: filtering by status shows the stale card alone", async () => {
    const { root } = await mountFiltered(
      [
        repoCard("a", "active", minutesAgo(5)),
        repoCard("i", "idle", minutesAgo(60)),
        repoCard("s", "stale", minutesAgo(300)),
      ],
      "status=stale",
    );

    expect(names(root)).toEqual(["stale"]);
  });

  it("A card goes stale while filtered: it appears on the next tick, with no reload", async () => {
    let clock = NOW;
    const fetch = fakeFetch([repoCard("i", "idle", minutesAgo(200))]);
    const root = document.createElement("main");
    const app = await mountApp(root, {
      fetch,
      now: () => clock,
      filters: filtered("status=stale"),
    });
    expect(names(root)).toEqual([]);

    clock = new Date(NOW.getTime() + 60 * 60_000);
    app.updateTimes();

    expect(names(root)).toEqual(["idle"]);
    expect(fetch.urls).toEqual(["/api/changes"]);
  });

  it("A filter value that matches nothing: says so, with a way to clear the filters", async () => {
    const { root } = await mountFiltered(
      [repoCard("a", "panopticon")],
      "repo=github.com/emmz/nothing-here",
    );

    expect(names(root)).toEqual([]);
    expect(text(root, ".no-match h2")).toBe("No changes match these filters");
    q<HTMLButtonElement>(root, ".no-match button").click();

    expect(names(root)).toEqual(["panopticon"]);
    expect(root.querySelector(".no-match")).toBeNull();
  });

  it("matches nothing even with no changes at all, rather than the getting-started page", async () => {
    const { root } = await mountFiltered([], "machine=vm-1");

    expect(text(root, ".no-match h2")).toBe("No changes match these filters");
    expect(root.querySelector(".empty-state")).toBeNull();
  });

  it("Owner and person, then Clear all: every card is shown again", async () => {
    const mine = card({
      changeId: "m",
      change: "mine",
      agents: [
        {
          client: "claude-code",
          machine: "vm-1",
          lastReportedAt: NOW.toISOString(),
          member: "Emmz",
          memberId: EMMZ_ID,
        },
      ],
    });
    const theirs = card({ changeId: "t", change: "theirs" });
    const acme = card({
      changeId: "x",
      change: "acme",
      repo: { host: "github.com", owner: "acme", name: "site" },
    });
    const { root } = await mountFiltered(
      [mine, theirs, acme],
      `owner=github.com/emmz&member=${EMMZ_ID}&machine=vm-1`,
    );
    expect(names(root)).toEqual(["mine"]);

    q<HTMLButtonElement>(root, ".filter-clear").click();

    expect(names(root).sort()).toEqual(["acme", "mine", "theirs"]);
    expect(text(root, ".change-count")).toBe("3 changes");
  });

  it("Dashboard choices follow the cards: a report in a third repo adds it to the repo filter", async () => {
    const { root, app } = await mountFiltered([repoCard("a", "one"), repoCard("b", "two")], "");
    const repoOptions = () =>
      [...q<HTMLSelectElement>(root, "select[name=repo]").options].map((o) => o.textContent);
    expect(repoOptions()).toEqual(["All repos", "github.com/emmz/one", "github.com/emmz/two"]);

    app.upsert(repoCard("c", "three", NOW.toISOString()));

    expect(repoOptions()).toEqual([
      "All repos",
      "github.com/emmz/one",
      "github.com/emmz/three",
      "github.com/emmz/two",
    ]);
  });

  it("keeps a load error up through the clock's tick and a filter change", async () => {
    const { root, app } = await mount([], 500);

    app.updateTimes();
    app.setFilters(filtered("machine=vm-1"));

    expect(root.querySelector("[role=alert]")?.textContent).toContain("answered 500");
  });

  it("says when the address had a filter that didn't make sense", async () => {
    const { root } = await mountFiltered([card()], "repo=nope&machine=vm-1");

    expect(text(root, ".filter-note")).toBe(
      "Ignored a filter in the address that didn't make sense: repo.",
    );
    expect(q<HTMLSelectElement>(root, "select[name=machine]").value).toBe("vm-1");
  });
});

// Review fixes (filters-and-idle-cards §10).
describe("mountApp review fixes", () => {
  it("10.1 a live update after a failed load keeps the error up, and the next refresh shows its card", async () => {
    const root = document.createElement("main");
    const fetch = fakeFetch([], 500);
    const app = await mountApp(root, { fetch, now: () => NOW });

    app.upsert(repoCard("a", "panopticon", NOW.toISOString()));
    app.remove("nope");

    expect(root.querySelector("[role=alert]")?.textContent).toContain("answered 500");
    expect(cardsIn(root)).toHaveLength(0);

    fetch.status = 200;
    fetch.cards = [repoCard("a", "panopticon", NOW.toISOString())];
    await app.refresh();
    expect(cardsIn(root).map((c) => text(c, ".card-change"))).toEqual(["panopticon"]);
  });

  it("10.3 takes the deployment's thresholds when they arrive after the cards", async () => {
    const { root, app } = await mount([card({ lastReportedAt: minutesAgo(3) })]);
    expect(q(root, "article.card").dataset["quiet"]).toBe("active");

    app.setQuiet({ idleAfterSeconds: 60, staleAfterSeconds: 600 });

    expect(q(root, "article.card").dataset["quiet"]).toBe("idle");
  });

  it("10.5 the tick doesn't redraw the filter bar, so a choice in progress survives it", async () => {
    const { root, app } = await mount([repoCard("a", "one"), repoCard("b", "two")]);
    const select = q<HTMLSelectElement>(root, "select[name=repo]");
    const options = [...select.options];
    select.value = "github.com/emmz/two"; // moving through the select, not chosen yet

    app.updateTimes();

    expect(select.value).toBe("github.com/emmz/two");
    expect([...select.options]).toEqual(options);
  });

  it("10.2 a live update that changes nothing for a select leaves its value alone", async () => {
    const { root, app } = await mount([repoCard("a", "one"), repoCard("b", "two")]);
    const select = q<HTMLSelectElement>(root, "select[name=repo]");
    select.value = "github.com/emmz/two";

    app.upsert(repoCard("a", "one", NOW.toISOString()));

    expect(select.value).toBe("github.com/emmz/two");
  });

  it("10.4 an address with only a malformed filter offers Clear filters", async () => {
    const root = document.createElement("main");
    await mountApp(root, {
      fetch: fakeFetch([card()]),
      now: () => NOW,
      filters: filtered("member=not-a-member-id"),
    });

    expect(q<HTMLButtonElement>(root, ".filter-clear").hidden).toBe(false);
    q<HTMLButtonElement>(root, ".filter-clear").click();

    expect(q<HTMLButtonElement>(root, ".filter-clear").hidden).toBe(true);
    expect(q(root, ".filter-note").hidden).toBe(true);
  });
});

// Spec change-filters (projects): "Filter by Project", "Project filter choices", and live updates respecting it.
describe("mountApp Project filter", () => {
  const PAN = { id: "6f1c2e4a-0000-4000-8000-000000000001", code: "PAN", name: "Panopticon" };
  const ACME = { id: "6f1c2e4a-0000-4000-8000-000000000002", code: "ACME", name: "Acme" };
  const inProject = (id: string, project: typeof PAN | null, personal = false) => ({
    ...repoCard(id, id),
    project,
    personal,
  });

  async function mountFiltered(cards: ChangeCard[], query: string) {
    const root = document.createElement("main");
    const app = await mountApp(root, {
      fetch: fakeFetch(cards),
      now: () => NOW,
      filters: filtered(query),
    });
    return { root, app };
  }

  const names = (root: HTMLElement) => cardsIn(root).map((c) => text(c, ".card-change"));
  const projectOptions = (root: HTMLElement) =>
    [...q<HTMLSelectElement>(root, "select[name=project]").options].map((o) => [
      o.value,
      o.textContent,
    ]);

  it("One Project: filtering by PAN's id shows only PAN's cards", async () => {
    const { root } = await mountFiltered(
      [inProject("pan", PAN), inProject("acme", ACME), inProject("mine", null, true)],
      `project=${PAN.id.toUpperCase()}`,
    );

    expect(names(root)).toEqual(["pan"]);
    expect(q<HTMLSelectElement>(root, "select[name=project]").value).toBe(PAN.id);
  });

  it("Personal only: shows personal cards, not a Project's or a bootstrap reporter's", async () => {
    const { root } = await mountFiltered(
      [inProject("pan", PAN), inProject("mine", null, true), inProject("boot", null, false)],
      "project=Personal",
    );

    expect(names(root)).toEqual(["mine"]);
  });

  it("Someone else's Project in the address: nothing matches, and the page offers to clear", async () => {
    const { root } = await mountFiltered(
      [inProject("pan", PAN)],
      "project=6f1c2e4a-0000-4000-8000-0000000000ff",
    );

    expect(names(root)).toEqual([]);
    expect(text(root, ".no-match h2")).toBe("No changes match these filters");
    expect(projectOptions(root)[1]).toEqual([
      "6f1c2e4a-0000-4000-8000-0000000000ff",
      "Unknown Project (no changes)",
    ]);
  });

  it("drops a project value that isn't an id or personal", async () => {
    const { root } = await mountFiltered([inProject("pan", PAN)], "project=PAN");

    expect(names(root)).toEqual(["pan"]);
  });

  it("Only Projects with changes: offered by code and name, by code, with Personal last", async () => {
    const { root } = await mountFiltered(
      [inProject("pan", PAN), inProject("acme", ACME), inProject("mine", null, true)],
      "",
    );

    expect(projectOptions(root)).toEqual([
      ["", "All Projects"],
      [ACME.id, "ACME: Acme"],
      [PAN.id, "PAN: Panopticon"],
      ["personal", "Personal"],
    ]);
  });

  it("offers no Personal without personal work, and names a Project as on its newest card", async () => {
    const { root } = await mountFiltered(
      [
        { ...inProject("old", PAN), lastReportedAt: minutesAgo(60) },
        {
          ...inProject("new", { ...PAN, name: "Panopticon server" }),
          lastReportedAt: minutesAgo(1),
        },
      ],
      "",
    );

    expect(projectOptions(root)).toEqual([
      ["", "All Projects"],
      [PAN.id, "PAN: Panopticon server"],
    ]);
  });

  it("Live updates respect the Project filter: another Project's report stays hidden, PAN's shows", async () => {
    const { root, app } = await mountFiltered([inProject("pan", PAN)], `project=${PAN.id}`);

    app.upsert({ ...inProject("acme", ACME), lastReportedAt: NOW.toISOString() });
    expect(names(root)).toEqual(["pan"]);
    expect(text(root, ".change-count")).toBe("1 of 2 changes");

    app.upsert({ ...inProject("pan2", PAN), lastReportedAt: NOW.toISOString() });
    expect(names(root)).toEqual(["pan2", "pan"]);
  });
});
