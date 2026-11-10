import { el } from "./dom";
import type { CardProject } from "./types";

/**
 * A Project's code plate (web/DESIGN.md "Projects"), or the dashed "Personal" plate for work in no Project. The code
 * is set as text.
 */
export function codePlate(code: string | null, className?: string): HTMLSpanElement {
  return el(
    "span",
    ["project-code", code === null && "personal", className].filter(Boolean).join(" "),
    code ?? "Personal",
  );
}

/**
 * The label a card, change page or history row shows (projects D9): its Project's code, with the name as a tooltip;
 * "Personal" for its owner's personal work; or nothing for a bootstrap reporter's change, which is neither.
 */
export function projectLabel(item: {
  project: CardProject | null;
  personal: boolean;
}): HTMLSpanElement | null {
  if (item.project) {
    const plate = codePlate(item.project.code);
    plate.title = item.project.name;
    return plate;
  }

  return item.personal ? codePlate(null) : null;
}

/** The label and the space after it, to lead a line of text; nothing when there's no label. */
export function leadingProjectLabel(item: {
  project: CardProject | null;
  personal: boolean;
}): (HTMLSpanElement | string)[] {
  const label = projectLabel(item);
  return label ? [label, " "] : [];
}
