import { describe, expect, it } from "vitest";
import { tagChips, tagText } from "./tagChips";

const PAN = { id: "p1", code: "PAN", name: "Panopticon" };
const BUG = { id: "t1", key: "bug", value: null };
const AREA = { id: "t2", key: "area", value: "onboarding" };

const chips = (list: HTMLElement | null) =>
  [...(list?.querySelectorAll<HTMLElement>(".tag-chip") ?? [])].map((chip) => ({
    lead: chip.querySelector(".tag-chip-lead")?.textContent ?? "",
    main: chip.querySelector(".tag-chip-main")?.textContent,
    personal: chip.classList.contains("personal"),
  }));

// Spec tags: "Tags shown on changes", "Shown with the code" (web/DESIGN.md "Tags").
describe("tagChips", () => {
  it("Both kinds: the project tags with the code, then the personal ones, dashed", () => {
    const list = tagChips({
      project: PAN,
      projectTags: [AREA, BUG],
      personalTags: [
        { key: "dev", value: null },
        { key: "language", value: "csharp" },
      ],
    });

    expect(list?.getAttribute("aria-label")).toBe("Tags");
    expect(chips(list)).toEqual([
      { lead: "PAN/area: ", main: "onboarding", personal: false },
      { lead: "PAN/", main: "bug", personal: false },
      { lead: "", main: "dev", personal: true },
      { lead: "language: ", main: "csharp", personal: true },
    ]);
    expect(list?.querySelector(".tag-chip")?.textContent).toBe("PAN/area: onboarding");
  });

  it("a personal chip tells a screen reader, and its tooltip, that only you see it", () => {
    const list = tagChips({
      project: null,
      projectTags: [],
      personalTags: [{ key: "dev", value: null }],
    });

    const chip = list?.querySelector<HTMLElement>(".tag-chip.personal");
    expect(chip?.textContent).toBe("dev (only you see this)");
    expect(chip?.querySelector(".visually-hidden")?.textContent).toBe(" (only you see this)");
    expect(chip?.title).toBe("Only you see this tag");
  });

  it("No tags: no row at all", () => {
    expect(tagChips({ project: PAN, projectTags: [], personalTags: [] })).toBeNull();
  });

  it("past the limit, the rest fold into +N more, which names them", () => {
    const personalTags = Array.from({ length: 10 }, (_, i) => ({
      key: `tag-${i + 1}`,
      value: null,
    }));

    const list = tagChips({ project: PAN, projectTags: [BUG], personalTags }, 8);

    expect(list?.querySelectorAll(".tag-chip")).toHaveLength(8);
    const more = list?.querySelector<HTMLElement>(".tag-chips-more");
    expect(more?.textContent).toBe("+3 more");
    expect(more?.title).toBe("tag-8, tag-9, tag-10");
  });

  it("at exactly the limit there's no +N more", () => {
    const personalTags = Array.from({ length: 8 }, (_, i) => ({
      key: `tag-${i + 1}`,
      value: null,
    }));

    expect(
      tagChips({ project: null, projectTags: [], personalTags }, 8)?.querySelector(
        ".tag-chips-more",
      ),
    ).toBeNull();
  });

  it("tag text is set as text, never markup", () => {
    const list = tagChips({
      project: PAN,
      projectTags: [{ id: "x", key: "k", value: "<b>x</b>" }],
      personalTags: [],
    });

    expect(list?.querySelector("b")).toBeNull();
    expect(list?.textContent).toBe("PAN/k: <b>x</b>");
  });

  it("tagText words a tag as the spec does", () => {
    expect(tagText(AREA, "PAN")).toBe("PAN/area: onboarding");
    expect(tagText(BUG, "PAN")).toBe("PAN/bug");
    expect(tagText({ key: "dev", value: null })).toBe("dev");
  });
});
