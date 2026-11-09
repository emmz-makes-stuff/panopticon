import { describe, expect, it } from "vitest";
import {
  filterChoices,
  filterOptions,
  matches,
  parseFilters,
  toQuery,
  type Filters,
} from "./filters";
import { card } from "./testing";
import type { CardAgent } from "./types";

const EMMZ = "0190f5a2-0000-7000-8000-000000000001";
const SAM = "0190f5a2-0000-7000-8000-000000000002";

const agent = (overrides: Partial<CardAgent> = {}): CardAgent => ({
  client: "claude-code",
  machine: "vm-1",
  lastReportedAt: "2026-11-08T11:57:00Z",
  member: "Emmz",
  memberId: EMMZ,
  ...overrides,
});

const parse = (query: string) => parseFilters(new URLSearchParams(query));

// Spec change-filters, design D4.
describe("parseFilters", () => {
  it("reads a repo as host, owner and name, with any middle segments in the owner (a subgroup)", () => {
    expect(parse("repo=github.com/emmz/panopticon").filters.repo).toEqual({
      host: "github.com",
      owner: "emmz",
      name: "panopticon",
    });
    expect(parse("repo=gitlab.com%2Fteam%2Fweb%2Fapp").filters.repo).toEqual({
      host: "gitlab.com",
      owner: "team/web",
      name: "app",
    });
  });

  it("reads an owner as host and owner, keeping a subgroup whole", () => {
    expect(parse("owner=gitlab.com/team/web").filters.owner).toEqual({
      host: "gitlab.com",
      owner: "team/web",
    });
  });

  it.each([
    ["repo=github.com/emmz", "repo"],
    ["repo=github.com//panopticon", "repo"],
    ["owner=github.com", "owner"],
    ["owner=github.com/", "owner"],
    ["member=not-an-id", "member"],
    ["status=sleepy", "status"],
  ] as const)("drops a malformed value: %s", (query, key) => {
    const { filters, dropped } = parse(query);

    expect(filters[key]).toBeUndefined();
    expect(dropped).toEqual([key]);
  });

  it("ignores unknown keys and blank values", () => {
    expect(parse("doc=design.md&repo=&machine=%20")).toEqual({ filters: {}, dropped: [] });
  });

  it("reads member, machine, client and status", () => {
    expect(
      parse(`member=${EMMZ.toUpperCase()}&machine=vm-1&client=claude-code&status=stale`).filters,
    ).toEqual({ member: EMMZ, machine: "vm-1", client: "claude-code", status: "stale" });
  });
});

describe("toQuery", () => {
  it("writes the keys in a fixed order, whatever order they were given in", () => {
    const filters: Filters = {
      status: "idle",
      client: "cursor",
      machine: "vm-1",
      member: EMMZ,
      owner: { host: "github.com", owner: "emmz" },
      repo: { host: "github.com", owner: "emmz", name: "panopticon" },
    };

    expect(toQuery(filters)).toBe(
      `repo=github.com%2Femmz%2Fpanopticon&owner=github.com%2Femmz&member=${EMMZ}&machine=vm-1&client=cursor&status=idle`,
    );
  });

  it("is empty with no filters, and can leave out keys (the history has no status)", () => {
    expect(toQuery({})).toBe("");
    expect(toQuery({ status: "stale", machine: "vm-1" }, ["machine"])).toBe("machine=vm-1");
  });

  it.each([
    "repo=gitlab.com%2Fteam%2Fweb%2Fapp&status=active",
    `owner=github.com%2Femmz&member=${EMMZ}&client=claude-code`,
    "machine=My+Laptop",
  ])("round-trips through parseFilters: %s", (query) => {
    expect(toQuery(parse(query).filters)).toBe(query);
  });
});

// Spec change-filters: Filter by repo or owner, person, machine or agent type, status; Filters combine.
describe("matches", () => {
  const panopticon = card({ agents: [agent()] });
  const subgroup = card({ repo: { host: "gitlab.com", owner: "team/web", name: "app" } });
  const flat = card({ repo: { host: "gitlab.com", owner: "team", name: "app" } });

  it("One repo, matched without regard to case", () => {
    expect(matches(panopticon, parse("repo=GitHub.com/Emmz/Panopticon").filters)).toBe(true);
    expect(matches(panopticon, parse("repo=github.com/emmz/other").filters)).toBe(false);
  });

  it("One owner across repos, and an owner with a subgroup matched whole", () => {
    const other = card({ repo: { host: "github.com", owner: "emmz", name: "other" } });
    const acme = card({ repo: { host: "github.com", owner: "acme", name: "site" } });
    const emmz = parse("owner=github.com/emmz").filters;
    expect([panopticon, other, acme].map((c) => matches(c, emmz))).toEqual([true, true, false]);

    const web = parse("owner=gitlab.com/team/web").filters;
    expect([subgroup, flat].map((c) => matches(c, web))).toEqual([true, false]);
  });

  it("A person matches when any agent reported with their token (a shared change matches either)", () => {
    const shared = card({ agents: [agent(), agent({ member: "Sam", memberId: SAM })] });
    const samOnly = card({ agents: [agent({ member: "Sam", memberId: SAM })] });
    const bootstrap = card({ agents: [agent({ member: null, memberId: null })] });

    expect([shared, samOnly, bootstrap].map((c) => matches(c, { member: EMMZ }))).toEqual([
      true,
      false,
      false,
    ]);
    expect(matches(shared, { member: SAM })).toBe(true);
  });

  it("One machine, one agent type: any agent counts, without regard to case", () => {
    const two = card({ agents: [agent(), agent({ machine: "laptop", client: "cursor" })] });

    expect(matches(two, { machine: "LAPTOP" })).toBe(true);
    expect(matches(two, { client: "Cursor" })).toBe(true);
    expect(matches(two, { machine: "vm-2" })).toBe(false);
  });

  it("Status compares with the card's state, and is skipped without one", () => {
    expect(matches(panopticon, { status: "stale" }, "stale")).toBe(true);
    expect(matches(panopticon, { status: "stale" }, "idle")).toBe(false);
    expect(matches(panopticon, { status: "stale" })).toBe(true);
  });

  it("Filters combine: a change must match every one", () => {
    const filters = parse(`owner=github.com/emmz&member=${EMMZ}`).filters;

    expect(matches(panopticon, filters)).toBe(true);
    expect(matches(card({ agents: [agent({ memberId: SAM })] }), filters)).toBe(false);
    expect(matches(flat, filters)).toBe(false);
    expect(matches(panopticon, {})).toBe(true);
  });
});

// Spec change-filters, "Filter choices" (design D5).
describe("filterChoices", () => {
  it("gives the distinct values on the cards, sorted without regard to case", () => {
    const choices = filterChoices([
      card({
        changeId: "a",
        repo: { host: "github.com", owner: "emmz", name: "panopticon" },
        agents: [agent({ machine: "vm-2" }), agent({ machine: "Laptop", client: "cursor" })],
      }),
      card({
        changeId: "b",
        repo: { host: "github.com", owner: "acme", name: "site" },
        agents: [agent({ machine: "VM-2", member: "Sam", memberId: SAM })],
      }),
      card({
        changeId: "c",
        repo: { host: "GitHub.com", owner: "Emmz", name: "Panopticon" },
        agents: [agent({ member: null, memberId: null, client: "Claude-Code" })],
      }),
    ]);

    expect(choices.repos.map((r) => `${r.host}/${r.owner}/${r.name}`)).toEqual([
      "github.com/acme/site",
      "github.com/emmz/panopticon",
    ]);
    expect(choices.owners.map((o) => `${o.host}/${o.owner}`)).toEqual([
      "github.com/acme",
      "github.com/emmz",
    ]);
    expect(choices.members).toEqual([
      { id: EMMZ, displayName: "Emmz" },
      { id: SAM, displayName: "Sam" },
    ]);
    expect(choices.machines).toEqual(["Laptop", "vm-1", "vm-2"]);
    expect(choices.clients.map((c) => c.toLowerCase())).toEqual(["claude-code", "cursor"]);
  });

  it("names a member as on their most recent card, and adds no person for an agent with no member", () => {
    const choices = filterChoices([
      card({ changeId: "old", lastReportedAt: "2026-11-08T10:00:00Z", agents: [agent()] }),
      card({
        changeId: "new",
        lastReportedAt: "2026-11-08T11:00:00Z",
        agents: [agent({ member: "Emmz R" }), agent({ member: null, memberId: null })],
      }),
    ]);

    expect(choices.members).toEqual([{ id: EMMZ, displayName: "Emmz R" }]);
  });
});

describe("filterOptions", () => {
  it("keeps a chosen value no change has, marked as having no changes", () => {
    const choices = filterChoices([card({ agents: [agent()] })]);

    const options = filterOptions(choices, {
      repo: { host: "github.com", owner: "emmz", name: "gone" },
      member: SAM,
      machine: "VM-1",
    });

    expect(options.repo[0]).toEqual({
      value: "github.com/emmz/gone",
      label: "github.com/emmz/gone (no changes)",
      missing: true,
    });
    expect(options.member[0]).toMatchObject({ value: SAM, label: "Unknown member (no changes)" });
    // A different case of a value on the page is that value, not a missing one.
    expect(options.machine).toEqual([{ value: "vm-1", label: "vm-1" }]);
  });
});

// Review fix 10.7: the same order as the server's OrdinalIgnoreCase, field by field.
describe("filterChoices order", () => {
  it("sorts host, owner, then name, comparing upper-cased code units as the server does", () => {
    const choices = filterChoices([
      card({ changeId: "1", repo: { host: "github.com", owner: "a/b", name: "x" } }),
      card({ changeId: "2", repo: { host: "github.com", owner: "a-b", name: "x" } }),
      card({ changeId: "3", repo: { host: "github.com", owner: "a_b", name: "x" } }),
      card({ changeId: "4", repo: { host: "github.com", owner: "A", name: "x" } }),
    ]);

    // Upper-cased: '-' (2D) < '/' (2F) < 'B' (42) < '_' (5F).
    expect(choices.owners.map((o) => o.owner)).toEqual(["A", "a-b", "a/b", "a_b"]);
    expect(choices.repos.map((r) => r.owner)).toEqual(["A", "a-b", "a/b", "a_b"]);
  });
});
