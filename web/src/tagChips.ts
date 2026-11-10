import { el } from "./dom";
import type { CardProject, PersonalTagRef, ProjectTagRef } from "./types";

/** How many chips a card shows before "+N more" (web/DESIGN.md "Tags"). */
export const CARD_TAG_LIMIT = 8;

/** A tag as text, as the spec words it: "PAN/area: onboarding" for a Project's, "dev" for a personal one. */
export function tagText(tag: { key: string; value: string | null }, code?: string | null): string {
  const text = tag.value === null ? tag.key : `${tag.key}: ${tag.value}`;
  return code ? `${code}/${text}` : text;
}

/**
 * A tag's chip (web/DESIGN.md "Tags"). The part that tells tags apart (the value, or a bare key) is ink, and what comes
 * before it is muted. A personal chip is dashed, and says to a screen reader that only you see it. All text goes in as
 * text.
 */
export function tagChip(
  tag: { key: string; value: string | null },
  code: string | null,
  personal: boolean,
): HTMLSpanElement {
  const lead = `${code ? `${code}/` : ""}${tag.value === null ? "" : `${tag.key}: `}`;
  const chip = el(
    "span",
    personal ? "tag-chip personal" : "tag-chip",
    lead && el("span", "tag-chip-lead", lead),
    el("span", "tag-chip-main", tag.value ?? tag.key),
  );
  if (personal) {
    chip.title = "Only you see this tag";
    chip.append(el("span", "visually-hidden", " (only you see this)"));
  }

  return chip;
}

/**
 * A change's tags as a row of chips (tags D9): its Project's tags with the code, then the viewer's personal ones.
 * Null when it has none, so the change looks as it did before tags. With `max`, chips past it fold into "+N more",
 * which lists them in its title.
 */
export function tagChips(
  item: {
    project: CardProject | null;
    projectTags: ProjectTagRef[];
    personalTags: PersonalTagRef[];
  },
  max?: number,
): HTMLUListElement | null {
  const code = item.project?.code ?? null;
  const all = [
    ...item.projectTags.map((tag) => ({ tag, code, personal: false })),
    ...item.personalTags.map((tag) => ({ tag, code: null, personal: true })),
  ];
  if (all.length === 0) {
    return null;
  }

  const shown = max === undefined ? all : all.slice(0, max);
  const list = el("ul", "tag-chips");
  list.setAttribute("aria-label", "Tags");
  for (const { tag, code: prefix, personal } of shown) {
    list.append(el("li", undefined, tagChip(tag, prefix, personal)));
  }

  const rest = all.slice(shown.length);
  if (rest.length > 0) {
    const more = el("li", "tag-chips-more", `+${rest.length} more`);
    more.title = rest.map(({ tag, code: prefix }) => tagText(tag, prefix)).join(", ");
    list.append(more);
  }

  return list;
}
