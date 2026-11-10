import { beforeEach, describe, expect, it } from "vitest";
import { PROJECTS_CHANGED } from "../live";
import { card, FakeEventSource, q, settle } from "../testing";
import { signedIn, startTestShell } from "../testShell";

beforeEach(() => {
  FakeEventSource.instances.length = 0;
});

const choose = (root: HTMLElement, name: string, value: string) => {
  const select = q<HTMLSelectElement>(root, `select[name=${name}]`);
  select.value = value;
  select.dispatchEvent(new Event("change"));
};
const names = (root: HTMLElement) =>
  [...root.querySelectorAll("article.card .card-change")].map((c) => c.textContent);

const twoRepos = () =>
  signedIn().on("GET", "/api/changes", {
    status: 200,
    body: [
      card({ changeId: "a", change: "team-auth" }),
      card({
        changeId: "b",
        change: "site",
        repo: { host: "github.com", owner: "acme", name: "site" },
        agents: [{ client: "cursor", machine: "laptop", lastReportedAt: "2026-11-08T11:57:00Z" }],
      }),
    ],
  });

// Spec change-filters, "Filters in the address" (design D4).
describe("cardsView filters", () => {
  it("writes a choice to the address, in key order, with no remount, refetch or reconnect", async () => {
    const server = twoRepos();
    const { root, nav } = await startTestShell("/", server);
    const grid = q(root, ".grid");

    choose(root, "machine", "laptop");
    await settle();
    choose(root, "owner", "github.com/acme");
    await settle();

    expect(nav.path()).toBe("/?owner=github.com%2Facme&machine=laptop");
    expect(names(root)).toEqual(["site"]);
    expect(q(root, ".grid")).toBe(grid);
    expect(server.to("GET", "/api/changes")).toHaveLength(1);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.latest().closed).toBe(false);
  });

  it("Back undoes a filter: the dashboard shows the filters it had before, with no reload", async () => {
    const server = twoRepos();
    const { root, nav, events } = await startTestShell("/?machine=laptop", server);
    expect(names(root)).toEqual(["site"]);

    choose(root, "client", "claude-code");
    await settle();
    expect(names(root)).toEqual([]);

    nav.history.replaceState(null, "", "/?machine=laptop");
    events.dispatchEvent(new Event("popstate"));
    await settle();

    expect(names(root)).toEqual(["site"]);
    expect(q<HTMLSelectElement>(root, "select[name=client]").value).toBe("");
    expect(server.to("GET", "/api/changes")).toHaveLength(1);
  });

  it("Share a filtered dashboard: opening a filtered address shows it filtered", async () => {
    const { root } = await startTestShell("/?owner=github.com%2Femmz", twoRepos());

    expect(names(root)).toEqual(["team-auth"]);
    expect(q<HTMLSelectElement>(root, "select[name=owner]").value).toBe("github.com/emmz");
    expect(q(root, "select[name=owner]").classList).toContain("filter-select--set");
    expect(q(root, "select[name=repo]").classList).not.toContain("filter-select--set");
    expect(q(root, ".change-count").textContent).toBe("1 of 2 changes");
  });

  it("Clear all: goes back to the plain dashboard address, showing every card", async () => {
    const { root, nav } = await startTestShell(
      "/?owner=github.com%2Femmz&machine=vm-1&client=claude-code",
      twoRepos(),
    );

    q<HTMLButtonElement>(root, ".filter-clear").click();
    await settle();

    expect(nav.path()).toBe("/");
    expect(names(root)).toEqual(["team-auth", "site"]);
    expect(q<HTMLButtonElement>(root, ".filter-clear").hidden).toBe(true);
  });

  it("choosing All … removes that filter from the address", async () => {
    const { root, nav } = await startTestShell(
      "/?owner=github.com%2Femmz&machine=vm-1",
      twoRepos(),
    );

    choose(root, "owner", "");
    await settle();

    expect(nav.path()).toBe("/?machine=vm-1");
  });
});

// Review fix 10.3: the cards don't wait for /api/settings.
describe("cardsView settings", () => {
  it("shows the cards while the settings are pending, then applies them", async () => {
    let answer: (response: Response) => void = () => undefined;
    const pending = new Promise<Response>((resolve) => (answer = resolve));
    const server = signedIn();
    const slow = Object.assign(
      (input: string, init?: RequestInit) =>
        input === "/api/settings" ? pending : server(input, init),
      { calls: server.calls, on: server.on, to: server.to },
    );

    const { root } = await startTestShell("/", slow);

    expect(q(root, "article.card").dataset["quiet"]).toBe("active");
    answer(new Response(JSON.stringify({ idleAfterSeconds: 60, staleAfterSeconds: 600 })));
    await settle();
    await settle();

    expect(q(root, "article.card").dataset["quiet"]).toBe("idle");
  });
});

// Spec change-visibility, "Membership changes take effect live" (projects D3, §11).
describe("cardsView membership changes", () => {
  const PAN = { id: "0F6A1C2E-0000-4000-8000-00000000A0A0", code: "PAN", name: "Panopticon" };
  const ACME = { id: "1b2c3d4e-0000-4000-8000-00000000ac0e", code: "ACME", name: "Acme" };
  const panCard = card({ changeId: "p", change: "pan-work", project: PAN });
  const acmeCard = card({ changeId: "a", change: "acme-work", project: ACME });
  const left = (projectId: string) =>
    FakeEventSource.latest().emit(PROJECTS_CHANGED, JSON.stringify({ projectId, member: false }));

  it("Removed while watching: the Project's cards and its filter choice go, with no reload", async () => {
    const server = signedIn().on("GET", "/api/changes", { status: 200, body: [panCard, acmeCard] });
    const { root } = await startTestShell("/", server);
    FakeEventSource.latest().open();
    await settle();
    const loads = server.to("GET", "/api/changes").length;

    left(PAN.id.toLowerCase());
    await settle();

    expect(names(root)).toEqual(["acme-work"]);
    const choices = [...q<HTMLSelectElement>(root, "select[name=project]").options].map(
      (o) => o.text,
    );
    expect(choices.some((text) => text.includes("PAN"))).toBe(false);
    expect(server.to("GET", "/api/changes")).toHaveLength(loads);
  });

  it("Removed while filtering on the Project: the filter is cleared from the address", async () => {
    const server = signedIn().on("GET", "/api/changes", { status: 200, body: [panCard, acmeCard] });
    const { root, nav } = await startTestShell(`/?project=${PAN.id.toLowerCase()}`, server);
    FakeEventSource.latest().open();
    await settle();
    expect(names(root)).toEqual(["pan-work"]);

    left(PAN.id);
    await settle();

    expect(nav.path()).toBe("/");
    expect(names(root)).toEqual(["acme-work"]);
  });

  it("Invited while watching: the dashboard refetches the cards, and the Project's show at once", async () => {
    let joined = false;
    const server = signedIn().on("GET", "/api/changes", () => ({
      status: 200,
      body: joined ? [panCard, acmeCard] : [acmeCard],
    }));
    const { root } = await startTestShell("/", server);
    FakeEventSource.latest().open();
    await settle();
    expect(names(root)).toEqual(["acme-work"]);

    joined = true;
    FakeEventSource.latest().emit(
      PROJECTS_CHANGED,
      JSON.stringify({ projectId: PAN.id, member: true }),
    );
    await settle();

    expect(names(root).sort()).toEqual(["acme-work", "pan-work"]);
    expect(FakeEventSource.latest().closed).toBe(false);
  });

  it("ignores another Project's or a malformed event", async () => {
    const server = signedIn().on("GET", "/api/changes", { status: 200, body: [panCard] });
    const { root } = await startTestShell("/", server);
    FakeEventSource.latest().open();
    await settle();

    left("someone-elses");
    FakeEventSource.latest().emit(PROJECTS_CHANGED, "not json");
    FakeEventSource.latest().emit(PROJECTS_CHANGED, JSON.stringify({ projectId: PAN.id }));
    await settle();

    expect(names(root)).toEqual(["pan-work"]);
  });
});
