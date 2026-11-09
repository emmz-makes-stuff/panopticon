import { describe, expect, it } from "vitest";
import { durationRule, durationText } from "./metricsPanel";

// web/DESIGN.md "Metrics": the duration rule, and a duration's short and long forms.
describe("duration rule", () => {
  it("is as long as its time against the longest, at least a sliver, and dashed while running", () => {
    expect(durationRule(3600, 3600).style.getPropertyValue("--share")).toBe("100%");
    expect(durationRule(900, 3600).style.getPropertyValue("--share")).toBe("25%");
    expect(durationRule(1, 3600).style.getPropertyValue("--share")).toBe("2%");
    expect(durationRule(0, 0).style.getPropertyValue("--share")).toBe("0%");
    expect(durationRule(60, 3600, true).classList.contains("running")).toBe(true);
    expect(durationRule(60, 3600).getAttribute("aria-hidden")).toBe("true");
  });

  it("shows a duration short, with the long form as its title and its screen-reader text", () => {
    const text = durationText(3 * 3600 + 1800);

    expect(text.title).toBe("3 hours 30 minutes");
    expect(text.querySelector('[aria-hidden="true"]')?.textContent).toBe("3h 30m");
    expect(text.querySelector(".visually-hidden")?.textContent).toBe("3 hours 30 minutes");
  });
});
