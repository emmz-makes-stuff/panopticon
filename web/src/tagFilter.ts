/**
 * The filter bar's Tags control (tags D7, D9; web/DESIGN.md "The Tags filter"): a button that opens a popover of
 * checkboxes, grouped "Yours" then by Project code, and a line of the chosen tags, each with a × to remove it. Each
 * toggle applies at once. It shows what it's given and reports the tags now asked for.
 */
import { button, el } from "./dom";
import {
  parseTagFilter,
  sortTags,
  tagFilterValue,
  type TagChoice,
  type TagFilter,
} from "./filters";
import { tagChip, tagText } from "./tagChips";

export interface TagFilterControl {
  /** The labelled button and its popover, for the bar's row of filters. */
  control: HTMLElement;
  /** "Showing changes with all of:" and the chosen tags' chips; hidden with none chosen. */
  chosenLine: HTMLElement;
  /** Shows `chosen` among `choices`. A list whose choices haven't changed keeps its elements, so focus stays put. */
  update(choices: readonly TagChoice[], chosen: readonly TagFilter[]): void;
}

let instances = 0;

export function createTagFilter(onChange: (tags: TagFilter[]) => void): TagFilterControl {
  const id = `tag-filter-${++instances}`;
  let chosen: TagFilter[] = [];
  let choices: readonly TagChoice[] = [];
  let shownChoices = "";
  let open = false;
  const boxes = new Map<string, HTMLInputElement>();

  const label = el("span", "filter-label", "Tags");
  label.id = `${id}-label`;
  const toggle = button(
    "Any tags",
    () => (open ? close(false) : openPopover()),
    "filter-select filter-tags-button",
  );
  toggle.id = `${id}-button`;
  toggle.setAttribute("aria-labelledby", `${label.id} ${toggle.id}`);
  toggle.setAttribute("aria-expanded", "false");
  toggle.setAttribute("aria-controls", `${id}-popover`);

  const search = document.createElement("input");
  search.type = "search";
  search.autocomplete = "off";
  search.id = `${id}-search`;
  const list = el("div", "tag-filter-list");
  const popover = el(
    "div",
    "tag-filter-popover",
    el("label", "field tag-filter-search", el("span", "field-label", "Find a tag"), search),
    list,
  );
  popover.id = `${id}-popover`;
  popover.hidden = true;
  popover.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      close(true);
    }
  });
  search.addEventListener("input", () => applySearch());

  const control = el("div", "filter filter-tags", label, toggle, popover);
  const chosenList = el("ul", "filter-tags-chips");
  chosenList.setAttribute("aria-label", "Chosen tags");
  const chosenLine = el(
    "div",
    "filter-tags-chosen",
    el("span", "filter-tags-lead", "Showing changes with all of:"),
    chosenList,
  );
  chosenLine.hidden = true;

  /** A click outside closes it, leaving focus where the click put it. */
  const outside = (event: Event) => {
    // Left the page while open: nothing to close, so just stop listening.
    if (!control.isConnected) {
      document.removeEventListener("click", outside, true);
      return;
    }

    if (!(event.target instanceof Node) || !control.contains(event.target)) {
      close(false);
    }
  };

  function openPopover(): void {
    open = true;
    popover.hidden = false;
    toggle.setAttribute("aria-expanded", "true");
    search.focus();
    document.addEventListener("click", outside, true);
  }

  function close(refocus: boolean): void {
    open = false;
    popover.hidden = true;
    toggle.setAttribute("aria-expanded", "false");
    search.value = "";
    applySearch();
    document.removeEventListener("click", outside, true);
    if (refocus) {
      toggle.focus();
    }
  }

  function setChosen(next: TagFilter[]): void {
    onChange(sortTags(next));
  }

  function toggleTag(filter: string, on: boolean): void {
    const tag = parseTagFilter(filter);
    if (!tag) {
      return;
    }

    const value = tagFilterValue(tag);
    const rest = chosen.filter((t) => tagFilterValue(t) !== value);
    setChosen(on ? [...rest, tag] : rest);
  }

  function renderList(): void {
    const focused = document.activeElement;
    const focusedFilter =
      focused instanceof HTMLInputElement && list.contains(focused) ? focused.value : undefined;
    boxes.clear();
    if (choices.length === 0) {
      list.replaceChildren(el("p", "tag-filter-empty", "None of these changes have tags."));
      return;
    }

    const groups: HTMLElement[] = [];
    for (const group of groupsOf(choices)) {
      const heading = el("p", "tag-filter-group", group.name);
      heading.id = `${id}-group-${groups.length}`;
      const options = group.choices.map((choice) => {
        const box = document.createElement("input");
        box.type = "checkbox";
        box.value = choice.filter;
        box.addEventListener("change", () => toggleTag(choice.filter, box.checked));
        boxes.set(choice.filter, box);
        const keyOnly = isKeyOnly(choice, group.choices);
        const option = el(
          "label",
          `tag-filter-option${choice.value !== null && hasKeyOnly(choice, group.choices) ? " tag-filter-option--value" : ""}`,
          box,
          el("span", "tag-filter-text", tagText(choice)),
          keyOnly ? el("span", "tag-filter-any", "any value") : null,
        );
        option.dataset["text"] = tagText(choice).toLowerCase();
        return option;
      });
      const element = el("div", "tag-filter-group-box", heading, ...options);
      element.setAttribute("role", "group");
      element.setAttribute("aria-labelledby", heading.id);
      groups.push(element);
    }

    list.replaceChildren(...groups);
    applySearch();
    if (focusedFilter !== undefined) {
      boxes.get(focusedFilter)?.focus();
    }
  }

  function applySearch(): void {
    const needle = search.value.trim().toLowerCase();
    for (const group of list.querySelectorAll<HTMLElement>(".tag-filter-group-box")) {
      let any = false;
      for (const option of group.querySelectorAll<HTMLElement>(".tag-filter-option")) {
        const hit = needle === "" || (option.dataset["text"] ?? "").includes(needle);
        option.hidden = !hit;
        any ||= hit;
      }

      group.hidden = !any;
    }
  }

  function renderChosen(): void {
    chosenList.replaceChildren(
      ...chosen.map((tag) => {
        const value = tagFilterValue(tag);
        const choice = choices.find((c) => c.filter === value);
        const { chip, text } = choice ? chosenChip(choice, choices) : unknownChip();
        const remove = button(
          "×",
          () => setChosen(chosen.filter((t) => tagFilterValue(t) !== value)),
          "filter-tags-remove",
        );
        remove.setAttribute("aria-label", `Remove ${text} from the filter`);
        return el("li", "filter-tags-chosen-tag", chip, remove);
      }),
    );
    chosenLine.hidden = chosen.length === 0;
  }

  return {
    control,
    chosenLine,
    update(nextChoices, nextChosen) {
      choices = nextChoices;
      chosen = [...nextChosen];
      const serialized = JSON.stringify(nextChoices);
      if (serialized !== shownChoices) {
        shownChoices = serialized;
        renderList();
      }

      const on = new Set(chosen.map(tagFilterValue));
      for (const [filter, box] of boxes) {
        box.checked = on.has(filter);
      }

      toggle.textContent =
        chosen.length === 0
          ? "Any tags"
          : `${chosen.length} ${chosen.length === 1 ? "tag" : "tags"}`;
      toggle.classList.toggle("filter-select--set", chosen.length > 0);
      renderChosen();
    },
  };
}

interface Group {
  name: string;
  choices: TagChoice[];
}

/** "Yours" (the personal tags), then one group per Project, in the order given (the API's). Empty groups don't show. */
function groupsOf(choices: readonly TagChoice[]): Group[] {
  const groups = new Map<string, Group>();
  for (const choice of choices) {
    const key = choice.project?.id.toLowerCase() ?? "";
    const group = groups.get(key) ?? { name: choice.project?.code ?? "Yours", choices: [] };
    group.choices.push(choice);
    groups.set(key, group);
  }

  return [...groups.values()];
}

const sameKey = (a: TagChoice, b: TagChoice) => a.key.toLowerCase() === b.key.toLowerCase();

/** A key offered alone: no value, and its group has values for that key (a personal bare tag stands for both). */
function isKeyOnly(choice: TagChoice, group: readonly TagChoice[]): boolean {
  return choice.value === null && group.some((c) => c.value !== null && sameKey(c, choice));
}

/** A value under a key offered alone, so it's indented beneath it. */
function hasKeyOnly(choice: TagChoice, group: readonly TagChoice[]): boolean {
  return group.some((c) => isKeyOnly(c, group) && sameKey(c, choice));
}

/** A chosen tag's chip: "PAN/bug", a dashed "dev", or for a key alone "PAN/language: any". */
function chosenChip(
  choice: TagChoice,
  all: readonly TagChoice[],
): { chip: HTMLElement; text: string } {
  const code = choice.project?.code ?? null;
  const group = all.filter(
    (c) => (c.project?.id.toLowerCase() ?? "") === (choice.project?.id.toLowerCase() ?? ""),
  );
  const shown = isKeyOnly(choice, group) ? { key: choice.key, value: "any" } : choice;
  return { chip: tagChip(shown, code, choice.project === null), text: tagText(shown, code) };
}

/** A tag the address names but no change here has: deleted, or a key nobody uses any more. */
function unknownChip(): { chip: HTMLElement; text: string } {
  return { chip: el("span", "tag-chip tag-chip--unknown", "Unknown tag"), text: "Unknown tag" };
}
