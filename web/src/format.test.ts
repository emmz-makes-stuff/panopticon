import { describe, expect, it } from "vitest";
import {
  calendarDate,
  countText,
  formatDuration,
  formatDurationLong,
  percent,
  relativeTime,
} from "./format";
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

// change-metrics D5: durations, short as shown and long as labelled.
describe("formatDuration and formatDurationLong", () => {
  const minute = 60;
  const hour = 3600;
  const day = 86400;

  it.each([
    [0, "<1m", "less than a minute"],
    [59, "<1m", "less than a minute"],
    [minute, "1m", "1 minute"],
    [45 * minute + 59, "45m", "45 minutes"],
    [59 * minute + 59, "59m", "59 minutes"],
    [hour, "1h", "1 hour"],
    [3 * hour + 30 * minute, "3h 30m", "3 hours 30 minutes"],
    [23 * hour + 59 * minute + 59, "23h 59m", "23 hours 59 minutes"],
    [day, "1d", "1 day"],
    [day + hour, "1d 1h", "1 day 1 hour"],
    [5 * day + 6 * hour + 59 * minute, "5d 6h", "5 days 6 hours"],
    [40 * day, "40d", "40 days"],
    [-30, "<1m", "less than a minute"],
  ])("%i seconds reads %s, or %s", (seconds, short, long) => {
    expect(formatDuration(seconds)).toBe(short);
    expect(formatDurationLong(seconds)).toBe(long);
  });
});

describe("calendarDate", () => {
  it("reads as a UTC date, with the year unless it's left out", () => {
    expect(calendarDate("2026-11-08T23:30:00-02:00")).toBe("9 Nov 2026");
    expect(calendarDate("2026-11-02T00:00:00Z", { year: false })).toBe("2 Nov");
  });
});
