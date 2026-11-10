import { describe, expect, it } from "vitest";
import {
  filterChoices,
  filterOptions,
  hasFilters,
  matches,
  MAX_FILTER_TAGS,
  parseFilters,
  toQuery,
  type Filters,
} from "./filters";
import { card } from "./testing";
import type { CardAgent, ChangeCard, PersonalTagRef, ProjectTagRef } from "./types";

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

// Spec change-filters, "Filter by tags" and "Filters combine" (tags D7).
describe("the Tags filter", () => {
  const PAN = { id: "0190f5a2-0000-7000-8000-0000000000a1", code: "PAN", name: "Panopticon" };
  const BUG = { id: "0190f5a2-0000-7000-8000-0000000000b1", key: "bug", value: null };
  const CSHARP = { id: "0190f5a2-0000-7000-8000-0000000000b2", key: "language", value: "csharp" };
  const RUST = { id: "0190f5a2-0000-7000-8000-0000000000b3", key: "language", value: "rust" };
  const tagged = (
    projectTags: ProjectTagRef[],
    personalTags: PersonalTagRef[] = [],
    overrides: Partial<ChangeCard> = {},
  ) => card({ project: PAN, projectTags, personalTags, ...overrides });
  const tagsIn = (query: string) => parse(query).filters.tags;

  describe("parseFilters", () => {
    it("reads the three forms, lower-casing ids, keys and values, with a value that can hold ':'", () => {
      const query = [
        `tag=t:${BUG.id.toUpperCase()}`,
        `tag=k:${PAN.id}:Language`,
        "tag=p:Dev",
        `tag=${encodeURIComponent("p:URL: https://x")}`,
      ].join("&");

      expect(tagsIn(query)).toEqual([
        { kind: "key", projectId: PAN.id, key: "language" },
        { kind: "personal", key: "dev", value: null },
        { kind: "personal", key: "url", value: "https://x" },
        { kind: "tag", id: BUG.id },
      ]);
      expect(parse(query).dropped).toEqual([]);
    });

    it("ignores blank values and repeats, and has no tags filter when none are chosen", () => {
      expect(tagsIn(`tag=&tag=p:dev&tag=p:DEV&tag=+`)).toEqual([
        { kind: "personal", key: "dev", value: null },
      ]);
      expect(parse("tag=").filters).toEqual({});
    });

    it.each([
      "x",
      "x:dev",
      "t:not-a-guid",
      `k:${PAN.id}`,
      "k:not-a-guid:area",
      `k:${PAN.id}:area:onboarding`,
      "p:",
      "p:bad key",
      "p:dev:",
      "T:dev",
    ])("drops a malformed value, keeping the rest: %s", (value) => {
      const parsed = parse(`tag=${encodeURIComponent(value)}&tag=p:dev`);
      expect(parsed.filters.tags).toEqual([{ kind: "personal", key: "dev", value: null }]);
      expect(parsed.dropped).toEqual(["tags"]);
    });

    it("keeps at most 20 tags, the first in the address, and says only the first were kept", () => {
      const query = Array.from(
        { length: 22 },
        (_, n) => `tag=p:k${String(n).padStart(2, "0")}`,
      ).join("&");
      const parsed = parse(query);

      expect(parsed.filters.tags).toHaveLength(MAX_FILTER_TAGS);
      expect(parsed.filters.tags?.at(-1)).toEqual({ kind: "personal", key: "k19", value: null });
      expect(parsed.dropped).toEqual([]);
      expect(parsed.tagsOverLimit).toBe(true);
      const twenty = parse(query.split("&").slice(0, 20).concat("tag=p:k00").join("&"));
      expect(twenty.tagsOverLimit).toBeUndefined();
    });
  });

  describe("toQuery", () => {
    it("writes the tags last, one tag each, in canonical order whatever order they were chosen in", () => {
      const filters: Filters = {
        tags: [
          { kind: "tag", id: BUG.id },
          { kind: "personal", key: "url", value: "a:b" },
          { kind: "key", projectId: PAN.id, key: "language" },
        ],
        machine: "vm-1",
      };

      expect(toQuery(filters)).toBe(
        `machine=vm-1&tag=k%3A${PAN.id}%3Alanguage&tag=p%3Aurl%3Aa%3Ab&tag=t%3A${BUG.id}`,
      );
      expect(toQuery(filters, ["machine"])).toBe("machine=vm-1");
      expect(toQuery(parse(toQuery(filters)).filters)).toBe(toQuery(filters));
    });

    it("counts as a filter only with a tag chosen", () => {
      expect(hasFilters({ tags: [{ kind: "tag", id: BUG.id }] })).toBe(true);
      expect(hasFilters({ tags: [] })).toBe(false);
      expect(hasFilters({ tags: [{ kind: "tag", id: BUG.id }] }, ["machine"])).toBe(false);
    });
  });

  describe("matches", () => {
    const both = tagged([BUG], [{ key: "language", value: "csharp" }]);
    const bugOnly = tagged([BUG]);
    const none = tagged([]);

    it("Two tags: only changes with both", () => {
      const filters = parse(`tag=t:${BUG.id}&tag=p:language:csharp`).filters;

      expect(matches(both, filters)).toBe(true);
      expect(matches(bugOnly, filters)).toBe(false);
      expect(matches(none, filters)).toBe(false);
    });

    it("Key alone: any value of that key, and not a change without it", () => {
      const filters = parse(`tag=k:${PAN.id}:LANGUAGE`).filters;

      expect(matches(tagged([CSHARP]), filters)).toBe(true);
      expect(matches(tagged([RUST, BUG]), filters)).toBe(true);
      expect(matches(bugOnly, filters)).toBe(false);
      // Another Project's language tag isn't this Project's key.
      expect(
        matches(
          tagged([CSHARP], [], { project: { ...PAN, id: "0190f5a2-0000-7000-8000-0000000000a2" } }),
          filters,
        ),
      ).toBe(false);
    });

    it("A personal key alone matches the bare tag and any value; with a value, only that value", () => {
      const dev = tagged([], [{ key: "Dev", value: null }]);
      const devValued = tagged([], [{ key: "dev", value: "Laptop" }]);

      expect(matches(dev, parse("tag=p:dev").filters)).toBe(true);
      expect(matches(devValued, parse("tag=p:dev").filters)).toBe(true);
      expect(matches(devValued, parse("tag=p:dev:laptop").filters)).toBe(true);
      expect(matches(dev, parse("tag=p:dev:laptop").filters)).toBe(false);
      // A Project tag with the same text isn't a personal one.
      expect(matches(tagged([{ ...BUG, key: "dev" }]), parse("tag=p:dev").filters)).toBe(false);
    });

    it("Removing one tag: the filter keeps the other", () => {
      const filters = parse(`tag=t:${BUG.id}&tag=p:dev`).filters;
      const rest = { ...filters, tags: (filters.tags ?? []).filter((t) => t.kind !== "personal") };

      expect(matches(bugOnly, filters)).toBe(false);
      expect(matches(bugOnly, rest)).toBe(true);
    });

    it("A deleted tag in the address: nothing matches", () => {
      const filters = parse("tag=t:0190f5a2-0000-7000-8000-0000000000ff").filters;

      expect([both, bugOnly, none].filter((c) => matches(c, filters))).toEqual([]);
    });

    it("Tags with another filter: only that repo's changes carrying the tag", () => {
      const filters = parse(`repo=github.com/emmz/panopticon&tag=t:${BUG.id}`).filters;
      const elsewhere = tagged([BUG], [], {
        repo: { host: "github.com", owner: "emmz", name: "meko" },
      });

      expect(matches(bugOnly, filters)).toBe(true);
      expect(matches(elsewhere, filters)).toBe(false);
      expect(matches(none, filters)).toBe(false);
    });
  });
});

// Spec change-filters, "Tag filter choices" (tags D7): the dashboard's, in the server's order.
describe("filterChoices tags", () => {
  const PAN = { id: "0190f5a2-0000-7000-8000-0000000000a1", code: "PAN", name: "Panopticon" };
  const ACME = { id: "0190f5a2-0000-7000-8000-0000000000a2", code: "acme", name: "Acme" };
  const filtersOf = (cards: ChangeCard[]) => filterChoices(cards).tags.map((c) => c.filter);

  it("Grouped choices: her personal tags first, a key with values also alone above them, then PAN/bug", () => {
    const choices = filterChoices([
      card({
        project: PAN,
        projectTags: [{ id: "0190f5a2-0000-7000-8000-0000000000b1", key: "bug", value: null }],
        personalTags: [
          { key: "language", value: "csharp" },
          { key: "dev", value: null },
        ],
      }),
    ]).tags;

    expect(choices).toEqual([
      { filter: "p:dev", project: null, key: "dev", value: null },
      { filter: "p:language", project: null, key: "language", value: null },
      { filter: "p:language:csharp", project: null, key: "language", value: "csharp" },
      { filter: "t:0190f5a2-0000-7000-8000-0000000000b1", project: PAN, key: "bug", value: null },
    ]);
  });

  it("orders each Project by code without regard to case, then key alone, bare tag and values by value", () => {
    const tag = (n: number, key: string, value: string | null) => ({
      id: `0190f5a2-0000-7000-8000-0000000000c${n}`,
      key,
      value,
    });
    expect(
      filtersOf([
        card({
          changeId: "p",
          project: PAN,
          projectTags: [
            tag(1, "lang", "rust"),
            tag(2, "lang", null),
            tag(3, "lang", "csharp"),
            tag(4, "area", "x"),
          ],
        }),
        card({ changeId: "a", project: ACME, projectTags: [tag(5, "zed", null)] }),
      ]),
    ).toEqual([
      "t:0190f5a2-0000-7000-8000-0000000000c5",
      `k:${PAN.id}:area`,
      "t:0190f5a2-0000-7000-8000-0000000000c4",
      `k:${PAN.id}:lang`,
      "t:0190f5a2-0000-7000-8000-0000000000c2",
      "t:0190f5a2-0000-7000-8000-0000000000c3",
      "t:0190f5a2-0000-7000-8000-0000000000c1",
    ]);
  });

  it("makes one choice of a tag on several cards, spelled as on the newest; personal casings are one", () => {
    const id = "0190f5a2-0000-7000-8000-0000000000b1";
    const choices = filterChoices([
      card({
        changeId: "old",
        lastReportedAt: "2026-11-08T10:00:00Z",
        project: PAN,
        projectTags: [{ id, key: "bug", value: null }],
        personalTags: [{ key: "Dev", value: null }],
      }),
      card({
        changeId: "new",
        lastReportedAt: "2026-11-08T11:00:00Z",
        project: PAN,
        projectTags: [{ id, key: "defect", value: null }],
        personalTags: [{ key: "dev", value: null }],
      }),
    ]).tags;

    expect(choices.map((c) => [c.filter, c.key])).toEqual([
      ["p:dev", "dev"],
      [`t:${id}`, "defect"],
    ]);
  });

  it("is empty with no tags", () => {
    expect(filtersOf([card()])).toEqual([]);
  });
});
