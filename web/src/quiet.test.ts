import { describe, expect, it } from "vitest";
import { DEFAULT_QUIET, isQuietSettings, quietState } from "./quiet";
import { NOW } from "./testing";

const ago = (seconds: number) => new Date(NOW.getTime() - seconds * 1000).toISOString();

// Spec live-dashboard, "Quiet cards" (design D2).
describe("quietState", () => {
  it.each([
    [5 * 60, "active"],
    [30 * 60 - 1, "active"],
    [30 * 60, "active"],
    [30 * 60 + 1, "idle"],
    [3600, "idle"],
    [4 * 3600, "idle"],
    [4 * 3600 + 1, "stale"],
    [5 * 3600, "stale"],
  ] as const)("a report %i s ago is %s, with the default thresholds", (seconds, state) => {
    expect(quietState(ago(seconds), NOW, DEFAULT_QUIET)).toBe(state);
  });

  it("a report time in the future (clock skew) is active", () => {
    expect(quietState(ago(-3600), NOW, DEFAULT_QUIET)).toBe("active");
  });

  it("follows the deployment's thresholds", () => {
    const settings = { idleAfterSeconds: 60, staleAfterSeconds: 300 };

    expect(quietState(ago(61), NOW, settings)).toBe("idle");
    expect(quietState(ago(301), NOW, settings)).toBe("stale");
  });
});

describe("isQuietSettings", () => {
  it.each([
    [{ idleAfterSeconds: 1800, staleAfterSeconds: 14400 }, true],
    [{ idleAfterSeconds: 1800 }, false],
    [{ idleAfterSeconds: 0, staleAfterSeconds: 14400 }, false],
    [{ idleAfterSeconds: 1800, staleAfterSeconds: 1800 }, false],
    [{ idleAfterSeconds: "1800", staleAfterSeconds: 14400 }, false],
    [null, false],
  ])("%j is %s", (value, valid) => {
    expect(isQuietSettings(value)).toBe(valid);
  });
});
