import { describe, expect, it } from "vitest";
import { countText, percent, relativeTime } from "./format";
import { NOW } from "./testing";

describe("percent and countText", () => {
  it("rounds to a whole percentage", () => {
    expect(percent({ done: 1, total: 3 })).toBe(33);
    expect(percent({ done: 2, total: 3 })).toBe(67);
    expect(countText({ done: 22, total: 30 })).toBe("22/30 (73%)");
  });

  it("has no percentage for an empty change", () => {
    expect(percent({ done: 0, total: 0 })).toBeNull();
  });
});

describe("relativeTime", () => {
  const ago = (seconds: number) => new Date(NOW.getTime() - seconds * 1000).toISOString();

  it.each([
    [0, "just now"],
    [59, "just now"],
    [60, "1m ago"],
    [59 * 60, "59m ago"],
    [3 * 3600, "3h ago"],
    [49 * 3600, "2d ago"],
    [-30, "just now"],
  ])("%i seconds ago reads %s", (seconds, expected) => {
    expect(relativeTime(ago(seconds), NOW)).toBe(expected);
  });
});
