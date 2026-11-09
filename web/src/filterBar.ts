/**
 * The filter bar (filters-and-idle-cards D7): a labelled select per dimension, each starting with an "All …" option, and
 * "Clear filters" while any filter is set. It shows what it's given and reports choices; the page decides what a
 * choice does (on the dashboard and the history, it changes the address).
 */
import { button, el } from "./dom";
import {
  chosenOption,
  filterValue,
  hasFilters,
  parseFilters,
  toQuery,
  type FilterKey,
  type FilterOption,
  type FilterOptions,
  type Filters,
} from "./filters";

interface Dimension {
  key: FilterKey;
  label: string;
  all: string;
}

const DIMENSIONS: readonly Dimension[] = [
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
  update(options: FilterOptions, filters: Filters, dropped?: readonly FilterKey[]): void;
}

export function createFilterBar(bar: FilterBarOptions): FilterBar {
  const element = el("form", "filter-bar");
  element.setAttribute("aria-label", "Filters");
  element.addEventListener("submit", (event) => event.preventDefault());
  let current: Filters = {};

  const selects = new Map<FilterKey, { select: HTMLSelectElement; options: string }>();
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
    selects.set(dimension.key, { select, options: "" });
    element.append(el("label", "filter", el("span", "filter-label", dimension.label), select));
  }

  const clear = button("Clear filters", () => bar.onChange({}), "link-button filter-clear");
  const note = el("p", "filter-note");
  note.setAttribute("role", "status");
  element.append(clear, note);

  return {
    element,
    update(options, filters, dropped = []) {
      current = filters;
      for (const dimension of DIMENSIONS) {
        const entry = selects.get(dimension.key);
        if (!entry) {
          continue;
        }

        const choices = dimension.key === "status" ? STATUS_OPTIONS : options[dimension.key];
        const serialized = JSON.stringify(choices);
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

        entry.select.value =
          chosenOption(choices, filterValue(filters, dimension.key))?.value ?? "";
        entry.select.classList.toggle("filter-select--set", entry.select.value !== "");
      }

      clear.hidden = !hasFilters(filters, bar.keys);
      const ignored = dropped.filter((key) => bar.keys.includes(key));
      note.textContent = ignored.length
        ? `Ignored ${ignored.length === 1 ? "a filter" : "filters"} in the address that didn't make sense: ${ignored.join(", ")}.`
        : "";
      note.hidden = ignored.length === 0;
    },
  };
}

function option(label: string, value: string): HTMLOptionElement {
  const element = document.createElement("option");
  element.value = value;
  element.textContent = label;
  return element;
}
