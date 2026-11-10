/**
 * The filter bar (filters-and-idle-cards D7): a labelled select per dimension, each starting with an "All …" option, and
 * "Clear filters" while any filter is set. It shows what it's given and reports choices; the page decides what a
 * choice does (on the dashboard and the history, it changes the address).
 */
import { button, el } from "./dom";
import { createTagFilter } from "./tagFilter";
import {
  chosenOption,
  filterValue,
  hasFilters,
  MAX_FILTER_TAGS,
  parseFilters,
  toQuery,
  type FilterKey,
  type SingleFilterKey,
  type FilterOption,
  type FilterOptions,
  type Filters,
} from "./filters";

interface Dimension {
  key: SingleFilterKey;
  label: string;
  all: string;
}

const DIMENSIONS: readonly Dimension[] = [
  { key: "project", label: "Project", all: "All Projects" },
  { key: "repo", label: "Repo", all: "All repos" },
  { key: "owner", label: "Owner", all: "All owners" },
  { key: "member", label: "Person", all: "Everyone" },
  { key: "machine", label: "Machine", all: "All machines" },
  { key: "client", label: "Agent type", all: "All agent types" },
  { key: "status", label: "Status", all: "Any status" },
];

const STATUS_OPTIONS: FilterOption[] = [
  { value: "active", label: "Active" },
  { value: "idle", label: "Idle" },
  { value: "stale", label: "Stale" },
];

export interface FilterBarOptions {
  /** The dimensions to offer, in `DIMENSIONS` order (the history has no status). */
  keys: readonly FilterKey[];
  /** A choice in the bar: the filters it now asks for. */
  onChange(filters: Filters): void;
}

export interface FilterBar {
  element: HTMLElement;
  /**
   * Shows `filters` chosen among `options`, and says which keys of the address were `dropped` as malformed. A select
   * whose options haven't changed keeps its element, so a live update doesn't close one that's open.
   */
  update(
    options: FilterOptions,
    filters: Filters,
    dropped?: readonly FilterKey[],
    tagsOverLimit?: boolean,
  ): void;
}

export function createFilterBar(bar: FilterBarOptions): FilterBar {
  const element = el("form", "filter-bar");
  element.setAttribute("aria-label", "Filters");
  element.addEventListener("submit", (event) => event.preventDefault());
  let current: Filters = {};

  /** Each select, with the options and chosen value it last showed. */
  const selects = new Map<
    FilterKey,
    { select: HTMLSelectElement; options: string; chosen: string | undefined }
  >();
  for (const dimension of DIMENSIONS.filter((d) => bar.keys.includes(d.key))) {
    const select = el("select", "filter-select");
    select.name = dimension.key;
    select.addEventListener("change", () => {
      const params = new URLSearchParams(toQuery(current, bar.keys));
      if (select.value) {
        params.set(dimension.key, select.value);
      } else {
        params.delete(dimension.key);
      }

      bar.onChange(parseFilters(params).filters);
    });
    selects.set(dimension.key, { select, options: "", chosen: undefined });
    element.append(el("label", "filter", el("span", "filter-label", dimension.label), select));
  }

  // tags D7: after Status, with its chosen tags on a line under the bar.
  const tags = bar.keys.includes("tags")
    ? createTagFilter((next) => {
        const filters = { ...current };
        if (next.length > 0) {
          filters.tags = next;
        } else {
          delete filters.tags;
        }

        bar.onChange(filters);
      })
    : undefined;
  if (tags) {
    element.append(tags.control);
  }

  const clear = button("Clear filters", () => bar.onChange({}), "link-button filter-clear");
  const note = el("p", "filter-note");
  note.setAttribute("role", "status");
  element.append(clear, ...(tags ? [tags.chosenLine] : []), note);

  return {
    element,
    update(options, filters, dropped = [], tagsOverLimit = false) {
      current = filters;
      tags?.update(options.tags, filters.tags ?? []);
      for (const dimension of DIMENSIONS) {
        const entry = selects.get(dimension.key);
        if (!entry) {
          continue;
        }

        const choices = dimension.key === "status" ? STATUS_OPTIONS : options[dimension.key];
        const serialized = JSON.stringify(choices);
        const chosen = chosenOption(choices, filterValue(filters, dimension.key))?.value ?? "";
        // Nothing changed for this select: leave it be, so a choice in progress (the keyboard moving through an open
        // select) survives a live update.
        if (serialized === entry.options && chosen === entry.chosen) {
          continue;
        }

        if (serialized !== entry.options) {
          entry.select.replaceChildren(
            option(dimension.all, ""),
            ...choices.map((choice) => {
              const element = option(choice.label, choice.value);
              if (choice.missing) {
                element.className = "filter-missing";
              }

              return element;
            }),
          );
          entry.options = serialized;
        }

        entry.select.value = chosen;
        entry.chosen = chosen;
        entry.select.classList.toggle("filter-select--set", entry.select.value !== "");
      }

      const ignored = dropped.filter((key) => bar.keys.includes(key));
      // A malformed filter is still in the address: clearing removes it too.
      clear.hidden = !hasFilters(filters, bar.keys) && ignored.length === 0;
      const others = ignored.filter((key) => key !== "tags");
      const notes = [
        others.length
          ? `Ignored ${others.length === 1 ? "a filter" : "filters"} in the address that didn't make sense: ${others.join(", ")}.`
          : "",
        ignored.includes("tags") ? "Dropped a tag filter that wasn't valid." : "",
        tags && tagsOverLimit ? `Kept the first ${MAX_FILTER_TAGS} tags.` : "",
      ].filter(Boolean);
      note.textContent = notes.join(" ");
      note.hidden = notes.length === 0;
    },
  };
}

function option(label: string, value: string): HTMLOptionElement {
  const element = document.createElement("option");
  element.value = value;
  element.textContent = label;
  return element;
}

/**
 * What a filtered page shows when nothing matches (spec change-filters, "A filter value that matches nothing"), the
 * same on the dashboard and the history: a heading, a hint, and a way to clear the filters.
 */
export function noMatch(heading: string, hint: string, clear: () => void): HTMLElement {
  return el(
    "section",
    "no-match",
    el("h2", undefined, heading),
    el("p", undefined, hint),
    button("Clear filters", clear, "link-button"),
  );
}
