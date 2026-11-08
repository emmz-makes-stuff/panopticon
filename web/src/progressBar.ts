import { countText, percent } from "./format";
import type { Progress } from "./types";

/** Above this many tasks the cells merge into one continuous fill (web/DESIGN.md, "Bars"). */
export const DENSE_ABOVE = 40;

/**
 * A labelled "lit cells" bar: one cell per task, lit when done, with the count beside the label. With no tasks it
 * shows the empty state instead of 0%.
 *
 * The cells are one `role="progressbar"` named by `accessibleName` (e.g. "Section 2 progress"), whose value is the done
 * count out of the total, with a spoken text like "2 of 5 tasks, 40 percent". Lit and unlit cells differ in fill and
 * outline, and the count is printed beside the label, so the state never relies on colour alone.
 */
export function renderProgressBar(
  label: string,
  progress: Progress,
  accessibleName: string,
): HTMLElement {
  const wrapper = document.createElement("div");
  wrapper.className = "bar";

  const heading = document.createElement("div");
  heading.className = "bar-label";
  const name = document.createElement("span");
  name.className = "bar-name";
  name.textContent = label;
  heading.append(name);
  wrapper.append(heading);

  if (progress.total === 0) {
    const empty = document.createElement("div");
    empty.className = "bar-empty";
    empty.textContent = "No tasks in tasks.md yet";
    wrapper.append(empty);
    return wrapper;
  }

  const count = document.createElement("span");
  count.className = "bar-count";
  count.textContent = countText(progress);
  heading.append(count);

  const cells = document.createElement("div");
  cells.className = progress.total > DENSE_ABOVE ? "cells dense" : "cells";
  cells.setAttribute("role", "progressbar");
  cells.setAttribute("aria-label", accessibleName);
  cells.setAttribute("aria-valuemin", "0");
  cells.setAttribute("aria-valuemax", String(progress.total));
  cells.setAttribute("aria-valuenow", String(progress.done));
  cells.setAttribute(
    "aria-valuetext",
    `${progress.done} of ${progress.total} tasks, ${percent(progress) ?? 0} percent`,
  );
  for (let i = 0; i < progress.total; i++) {
    const cell = document.createElement("span");
    cell.className = i < progress.done ? "cell lit" : "cell";
    cells.append(cell);
  }

  wrapper.append(cells);
  return wrapper;
}
