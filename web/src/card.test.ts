import { describe, expect, it } from "vitest";
import { renderCard } from "./card";
import { card, NOW } from "./testing";

const text = (root: ParentNode, selector: string) => root.querySelector(selector)?.textContent;
function query<T extends Element = HTMLElement>(root: ParentNode, selector: string): T {
  const found = root.querySelector<T>(selector);
  if (!found) {
    throw new Error(`No element matches ${selector}`);
  }

  return found;
}

const bar = (root: HTMLElement, kind: "section" | "overall") => query(root, `.bar-${kind}`);

// Spec live-dashboard, "Card content" (team-auth: the member on each agent row, design D17).
describe("renderCard agents and members", () => {
  it("Card shows identity and agents: the member's display name follows the agent, as its own element", () => {
    const element = renderCard(
      card({
        agents: [
          {
            client: "claude-code",
            machine: "vm-1",
            lastReportedAt: "2026-11-08T11:57:00Z",
            member: "Emmz",
          },
        ],
      }),
      NOW,
    );

    const row = query(element, ".card-agent");
    expect(row.textContent).toBe("claude-code@vm-1 Emmz");
    expect(text(row, ".card-agent-member")).toBe("Emmz");
  });

  it("Agent without a member: the row shows only the client and machine", () => {
    const element = renderCard(
      card({
        agents: [
          {
            client: "claude-code",
            machine: "vm-1",
            lastReportedAt: "2026-11-08T11:57:00Z",
            member: null,
          },
          { client: "cursor", machine: "laptop", lastReportedAt: "2026-11-08T11:00:00Z" },
        ],
      }),
      NOW,
    );

    expect([...element.querySelectorAll(".card-agent")].map((a) => a.textContent)).toEqual([
      "claude-code@vm-1",
      "cursor@laptop",
    ]);
    expect(element.querySelector(".card-agent-member")).toBeNull();
  });

  it("shows a member name with markup as text", () => {
    const element = renderCard(
      card({
        agents: [
          {
            client: "c",
            machine: "m",
            lastReportedAt: "2026-11-08T11:57:00Z",
            member: "<i>Sam</i>",
          },
        ],
      }),
      NOW,
    );

    expect(text(element, ".card-agent-member")).toBe("<i>Sam</i>");
    expect(element.querySelector(".card-agent i")).toBeNull();
  });
});

describe("renderCard content", () => {
  it("shows the repo with its host, the change, the agents and when it last reported", () => {
    const element = renderCard(
      card({
        agents: [
          { client: "claude-code", machine: "vm-1", lastReportedAt: "2026-11-08T11:57:00Z" },
          { client: "cursor", machine: "laptop", lastReportedAt: "2026-11-08T11:00:00Z" },
        ],
      }),
      NOW,
    );

    expect(text(element, ".card-host")).toBe("github.com");
    expect(text(element, ".card-repo-name")).toBe("emmz/panopticon");
    expect(text(element, ".card-change")).toBe("walking-skeleton");
    expect([...element.querySelectorAll(".card-agent")].map((a) => a.textContent)).toEqual([
      "claude-code@vm-1",
      "cursor@laptop",
    ]);
    const time = query<HTMLTimeElement>(element, "time");
    expect(time.textContent).toBe("3m ago");
    expect(time.dateTime).toBe("2026-11-08T11:57:00Z");
  });

  it("shows the current section's number and title", () => {
    const element = renderCard(card(), NOW);

    expect(text(bar(element, "section"), ".bar-name")).toBe("Section 2: Core");
  });

  it("puts every field in as text, never as HTML", () => {
    const element = renderCard(
      card({
        change: '<img src=x onerror="alert(1)">',
        repo: { host: "h", owner: "<b>o</b>", name: "n" },
      }),
      NOW,
    );

    expect(element.querySelector("img, b")).toBeNull();
    expect(text(element, ".card-change")).toBe('<img src=x onerror="alert(1)">');
  });

  // Spec task-progress "HTML in a task": a heading's markup must not reach the dashboard as markup.
  it("puts a section title containing HTML in as text", () => {
    const element = renderCard(
      card({
        currentSection: {
          number: 2,
          title: "<script>alert(1)</script><em>Core</em>",
          progress: { done: 2, total: 5 },
        },
      }),
      NOW,
    );

    expect(element.querySelector("script, em")).toBeNull();
    expect(text(bar(element, "section"), ".bar-name")).toBe(
      "Section 2: <script>alert(1)</script><em>Core</em>",
    );
  });
});

// Spec live-dashboard, "Two progress bars".
describe("renderCard bars", () => {
  it("reads 2/5 (40%) for the section and 7/20 (35%) overall, with one lit cell per done task", () => {
    const element = renderCard(card(), NOW);

    const section = bar(element, "section");
    expect(text(section, ".bar-count")).toBe("2/5 (40%)");
    expect(section.querySelectorAll(".cell")).toHaveLength(5);
    expect(section.querySelectorAll(".cell.lit")).toHaveLength(2);

    const overall = bar(element, "overall");
    expect(text(overall, ".bar-name")).toBe("Whole change");
    expect(text(overall, ".bar-count")).toBe("7/20 (35%)");
    expect(overall.querySelectorAll(".cell")).toHaveLength(20);
    expect(overall.querySelectorAll(".cell.lit")).toHaveLength(7);
  });

  it("shows both bars as empty, not 0%, when tasks.md has no tasks", () => {
    const element = renderCard(card({ currentSection: null, overall: { done: 0, total: 0 } }), NOW);

    for (const kind of ["section", "overall"] as const) {
      const b = bar(element, kind);
      expect(text(b, ".bar-empty")).toBe("No tasks in tasks.md yet");
      expect(b.querySelector(".cells")).toBeNull();
      expect(b.textContent).not.toContain("%");
    }

    expect(text(bar(element, "section"), ".bar-name")).toBe("No current section");
  });

  it("merges the cells into a continuous fill above 40 tasks", () => {
    const element = renderCard(card({ overall: { done: 38, total: 64 } }), NOW);

    expect(bar(element, "overall").querySelector(".cells")?.classList.contains("dense")).toBe(true);
    expect(bar(element, "section").querySelector(".cells")?.classList.contains("dense")).toBe(
      false,
    );
  });
});

// Spec tags: "Tags shown on changes" on a card (web/DESIGN.md "Tags").
describe("renderCard tags", () => {
  const PAN = { id: "p1", code: "PAN", name: "Panopticon" };

  it("Both kinds: chips sit under the change name, ahead of the bars", () => {
    const element = renderCard(
      card({
        project: PAN,
        projectTags: [{ id: "t1", key: "bug", value: null }],
        personalTags: [{ key: "dev", value: null }],
      }),
      NOW,
    );

    const tags = query(element, ".tag-chips");
    expect(tags.previousElementSibling?.classList.contains("card-change")).toBe(true);
    expect(tags.nextElementSibling?.classList.contains("bar-section")).toBe(true);
    expect([...tags.querySelectorAll(".tag-chip")].map((chip) => chip.textContent)).toEqual([
      "PAN/bug",
      "dev (only you see this)",
    ]);
  });

  it("No tags: the card has no tag row", () => {
    const element = renderCard(card(), NOW);

    expect(element.querySelector(".tag-chips")).toBeNull();
    expect(
      query(element, ".card-change").nextElementSibling?.classList.contains("bar-section"),
    ).toBe(true);
  });

  it("a card shows eight chips, then +N more", () => {
    const personalTags = Array.from({ length: 11 }, (_, i) => ({ key: `t${i}`, value: null }));

    const element = renderCard(card({ personalTags }), NOW);

    expect(element.querySelectorAll(".tag-chip")).toHaveLength(8);
    expect(text(element, ".tag-chips-more")).toBe("+3 more");
  });
});

// Spec change-visibility / projects D9: "Card shows its Project".
describe("renderCard Project label", () => {
  const PAN = { id: "p1", code: "PAN", name: "Panopticon" };

  it("Card shows its Project: the code plate leads the host line, with the name as its tooltip", () => {
    const element = renderCard(card({ project: PAN, personal: false }), NOW);

    const plate = query(element, ".card-host .project-code");
    expect(plate.textContent).toBe("PAN");
    expect(plate.title).toBe("Panopticon");
    expect(plate.classList.contains("personal")).toBe(false);
    expect(text(element, ".card-host")).toBe("PAN github.com");
  });

  it("personal work shows the Personal plate", () => {
    const element = renderCard(card({ project: null, personal: true }), NOW);

    expect(query(element, ".card-host .project-code.personal").textContent).toBe("Personal");
    expect(text(element, ".card-host")).toBe("Personal github.com");
  });

  it("a bootstrap reporter's change, in no Project and nobody's personal work, shows no label", () => {
    const element = renderCard(card({ project: null, personal: false }), NOW);

    expect(element.querySelector(".project-code")).toBeNull();
    expect(text(element, ".card-host")).toBe("github.com");
  });

  it("a code is set as text", () => {
    const element = renderCard(card({ project: { ...PAN, code: "<b>X</b>" } }), NOW);

    expect(query(element, ".project-code").textContent).toBe("<b>X</b>");
    expect(element.querySelector(".project-code b")).toBeNull();
  });
});
