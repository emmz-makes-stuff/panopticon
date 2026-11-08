import { describe, expect, it } from "vitest";
import { renderCard } from "./card";
import { renderProgressBar } from "./progressBar";
import { card, NOW } from "./testing";

// Spec live-dashboard, "Accessible progress".
describe("progress bar accessibility", () => {
  it("exposes the section bar's label and value, e.g. Section 2 progress, 40 percent", () => {
    const element = renderCard(card(), NOW);

    const bars = [...element.querySelectorAll('[role="progressbar"]')];
    expect(bars).toHaveLength(2);

    const section = bars[0];
    expect(section?.getAttribute("aria-label")).toBe("Section 2 progress");
    expect(section?.getAttribute("aria-valuenow")).toBe("2");
    expect(section?.getAttribute("aria-valuemin")).toBe("0");
    expect(section?.getAttribute("aria-valuemax")).toBe("5");
    expect(section?.getAttribute("aria-valuetext")).toBe("2 of 5 tasks, 40 percent");

    const overall = bars[1];
    expect(overall?.getAttribute("aria-label")).toBe("Whole change progress");
    expect(overall?.getAttribute("aria-valuetext")).toBe("7 of 20 tasks, 35 percent");
  });

  it("doesn't rely on colour alone: the count is printed and lit cells are marked apart", () => {
    const bar = renderProgressBar("Section 2: Core", { done: 2, total: 5 }, "Section 2 progress");

    expect(bar.querySelector(".bar-count")?.textContent).toBe("2/5 (40%)");
    const cells = [...bar.querySelectorAll(".cell")];
    expect(cells.map((c) => c.classList.contains("lit"))).toEqual([
      true,
      true,
      false,
      false,
      false,
    ]);
  });

  it("has no progressbar for an empty change, only its readable empty text", () => {
    const bar = renderProgressBar("Whole change", { done: 0, total: 0 }, "Whole change progress");

    expect(bar.querySelector('[role="progressbar"]')).toBeNull();
    expect(bar.textContent).toContain("No tasks in tasks.md yet");
  });
});
