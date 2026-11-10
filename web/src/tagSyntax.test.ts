import { describe, expect, it } from "vitest";
import { MAX_TAG_PART, parseTag, sameTag } from "./tagSyntax";

// Tag syntax (tags D1), worded as the server's TagText.
describe("parseTag", () => {
  it("reads a bare key, and a key and value split at the first ':', trimming both", () => {
    expect(parseTag("  dev ")).toEqual({ tag: { key: "dev", value: null } });
    expect(parseTag(" area :  onboarding ")).toEqual({ tag: { key: "area", value: "onboarding" } });
    expect(parseTag("url: https://x:8080")).toEqual({
      tag: { key: "url", value: "https://x:8080" },
    });
    expect(parseTag("a.b_c-1")).toEqual({ tag: { key: "a.b_c-1", value: null } });
  });

  it("keeps the longest key and value", () => {
    const part = "x".repeat(MAX_TAG_PART);
    expect(parseTag(`${part}:${part}`)).toEqual({ tag: { key: part, value: part } });
  });

  it.each([
    ["", "A tag can't be empty."],
    ["   ", "A tag can't be empty."],
    [":x", `":x": a tag needs a key before the ':'.`],
    ["bad key", `"bad key": a key can only have letters, digits, '-', '_' and '.'.`],
    ["dev:", `"dev:": there's no value after the ':'. Leave the ':' out for a tag with no value.`],
    [
      `${"k".repeat(MAX_TAG_PART + 1)}`,
      `"${"k".repeat(MAX_TAG_PART + 1)}": a key can be at most 40 characters.`,
    ],
    [
      `k:${"v".repeat(MAX_TAG_PART + 1)}`,
      `"k:${"v".repeat(MAX_TAG_PART + 1)}": a value can be at most 40 characters.`,
    ],
  ])("refuses %j", (text, error) => {
    expect(parseTag(text)).toEqual({ error });
  });

  it("refuses a line break in a value", () => {
    expect(parseTag("k:a\rb")).toEqual({ error: `"k:a\rb": a value can't contain a line break.` });
  });
});

describe("sameTag", () => {
  it("compares without regard to case, and a bare tag isn't a valued one", () => {
    expect(
      sameTag({ key: "Area", value: "Onboarding" }, { key: "area", value: "onboarding" }),
    ).toBe(true);
    expect(sameTag({ key: "dev", value: null }, { key: "DEV", value: null })).toBe(true);
    expect(sameTag({ key: "area", value: "x" }, { key: "area", value: "y" })).toBe(false);
    expect(sameTag({ key: "area", value: null }, { key: "area", value: "x" })).toBe(false);
  });
});
