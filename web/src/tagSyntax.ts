/**
 * Tag syntax (tags D1), the same rules and messages as the server's `TagText`, so the picker can offer "+ Add" or say
 * what's wrong without a round trip. The server checks again.
 */

/** The longest a key, or a value, may be. */
export const MAX_TAG_PART = 40;

export interface ParsedTag {
  key: string;
  value: string | null;
}

/** The tag in `text`, or the error naming it, worded as the server words it. */
export function parseTag(text: string): { tag: ParsedTag } | { error: string } {
  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return { error: "A tag can't be empty." };
  }

  const colon = trimmed.indexOf(":");
  const key = (colon < 0 ? trimmed : trimmed.slice(0, colon)).trim();
  const value = colon < 0 ? null : trimmed.slice(colon + 1).trim();
  const problem = keyProblem(key) ?? (value === null ? null : valueProblem(value));
  return problem ? { error: `"${trimmed}": ${problem}` } : { tag: { key, value } };
}

/** Whether two tags are the same tag: compared without regard to case (tags D1). */
export function sameTag(a: ParsedTag, b: ParsedTag): boolean {
  return (
    a.key.toLowerCase() === b.key.toLowerCase() &&
    (a.value ?? "").toLowerCase() === (b.value ?? "").toLowerCase()
  );
}

function keyProblem(key: string): string | null {
  if (key.length === 0) {
    return "a tag needs a key before the ':'.";
  }

  if (key.length > MAX_TAG_PART) {
    return `a key can be at most ${MAX_TAG_PART} characters.`;
  }

  return /^[A-Za-z0-9_.-]+$/.test(key)
    ? null
    : "a key can only have letters, digits, '-', '_' and '.'.";
}

function valueProblem(value: string): string | null {
  if (value.length === 0) {
    return "there's no value after the ':'. Leave the ':' out for a tag with no value.";
  }

  if (value.length > MAX_TAG_PART) {
    return `a value can be at most ${MAX_TAG_PART} characters.`;
  }

  return /[\r\n]/.test(value) ? "a value can't contain a line break." : null;
}
