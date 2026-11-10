import { createApi, fetchChanges, type Api, type FetchLike } from "./api";
import { applyQuiet, renderCard } from "./card";
import { createFilterBar, noMatch } from "./filterBar";
import {
  FILTER_KEYS,
  filterChoices,
  filterOptions,
  hasFilters,
  matches,
  type Filters,
  type ParsedFilters,
} from "./filters";
import { retagCard, type ProjectTagsChange } from "./live";
import { DEFAULT_QUIET, type QuietSettings } from "./quiet";
import type { ChangeCard } from "./types";

/** The product's name, exactly as written (web/DESIGN.md, "Name"). */
export const PRODUCT_NAME = "PANOPTICON";

/** Gap between consecutive cells lighting up after a report (web/DESIGN.md, principle 4). */
export const LIGHTING_STEP_MS = 60;

export type LiveStatus = "connecting" | "live" | "offline";

export interface AppDeps {
  /** The API to load cards through. Without one, an API over `fetch` is made. */
  api?: Api;
  fetch?: FetchLike;
  now?: () => Date;
  /**
   * Where to show the live status and change count. The signed-in shell passes its header's slot; without one, the
   * dashboard draws its own header (wordmark and status).
   */
  statusSlot?: HTMLElement;
  /** When cards go idle and stale (`GET /api/settings`). Without them, the server's defaults. */
  quiet?: QuietSettings;
  /** The filters to start with (from the address), and any the address had that didn't parse. */
  filters?: ParsedFilters;
  /**
   * A choice in the filter bar or a "Clear filters" button. The cards view writes the filters to the address, and the
   * router hands them back through `setFilters`. Without it, the filters apply straight away.
   */
  onFiltersChange?: (filters: Filters) => void;
}

export interface App {
  /**
   * Replaces the cards with `cards`, the server's current state. A card already shown with a newer report (an
   * event that arrived while the list was loading) is kept rather than rolled back.
   */
  render(cards: ChangeCard[]): void;
  /**
   * Applies one live update: updates the card in place, or adds a new card at the top. Ignores an update that is
   * no newer than the card already shown. Returns whether it was applied.
   */
  upsert(card: ChangeCard): boolean;
  /** Takes a change's card away: it was archived, and lives in the history now. Returns whether it was shown. */
  remove(changeId: string): boolean;
  /**
   * The viewer left a Project, or was removed (spec change-visibility, "Membership changes take effect live"): its cards
   * go, and a filter on it is cleared, so its choice goes from the bar too.
   */
  leaveProject(projectId: string): void;
  /**
   * A Project's tags changed (tags D6): its cards' tags are renamed by id, and the ones that are gone dropped, with no
   * reload. Under a filter, a card that no longer matches goes out. Returns how many cards changed.
   */
  retagProject(change: ProjectTagsChange): number;
  /** Loads every card from the server and renders them. Throws if the load fails, leaving the cards as they were. */
  refresh(): Promise<void>;
  /** Shows whether the live connection is up. */
  setStatus(status: LiveStatus): void;
  /**
   * Re-reads every card's "last reported" time and quiet state against the clock (the cards view calls it every
   * 30 s), so a card goes idle, then stale, with no report or reload. Under a status filter, a card that changes
   * state comes in or goes out.
   */
  updateTimes(): void;
  /**
   * Shows the cards that match `filters` (spec change-filters), keeping every card: a live update for a hidden change
   * is still applied, ready for when the filters change.
   */
  setFilters(filters: ParsedFilters): void;
  /** Uses the deployment's idle and stale thresholds from now on (they can arrive after the cards). */
  setQuiet(settings: QuietSettings): void;
}

interface Shown {
  card: ChangeCard;
  element: HTMLElement;
}

const reportedAt = (card: ChangeCard) => Date.parse(card.lastReportedAt);

/** Mounts the dashboard into `root`, then loads the cards. Resolves once the first load has rendered (or failed). */
export async function mountApp(root: HTMLElement, deps: AppDeps = {}): Promise<App> {
  const now = deps.now ?? (() => new Date());
  let quiet = deps.quiet ?? DEFAULT_QUIET;
  const api = deps.api ?? createApi({ fetch: deps.fetch });
  const shown = new Map<string, Shown>();

  const header = document.createElement("header");
  header.className = "top";
  const wordmark = document.createElement("h1");
  wordmark.className = "wordmark";
  wordmark.textContent = PRODUCT_NAME;
  const status = document.createElement("div");
  status.className = "status";
  const live = document.createElement("span");
  live.className = "live";
  live.setAttribute("role", "status");
  const dot = document.createElement("span");
  dot.className = "dot";
  dot.setAttribute("aria-hidden", "true");
  const liveText = document.createElement("span");
  live.append(dot, liveText);
  const count = document.createElement("span");
  count.className = "change-count";
  status.append(live, count);
  header.append(wordmark, status);
  if (deps.statusSlot) {
    deps.statusSlot.replaceChildren(status);
  }

  const content = document.createElement("div");
  content.className = "content";
  const grid = document.createElement("section");
  grid.className = "grid";
  grid.setAttribute("aria-label", "Changes");
  root.replaceChildren(...(deps.statusSlot ? [content] : [header, content]));

  let filters = deps.filters ?? { filters: {}, dropped: [] };
  /**
   * Whether a load has rendered. Until then, nothing redraws, so a load error stays up: live updates are kept for when
   * the next refresh renders, and the tick and filter changes just wait.
   */
  let loaded = false;
  /** Whether the filter bar needs redrawing: the cards or the filters changed. The tick alone doesn't. */
  let barStale = true;
  let choices = filterChoices([]);
  const changeFilters =
    deps.onFiltersChange ?? ((next) => app.setFilters({ filters: next, dropped: [] }));
  const bar = createFilterBar({
    keys: FILTER_KEYS,
    onChange: (next) => changeFilters(next),
  });
  const noMatchBlock = noMatch(
    "No changes match these filters",
    "Choose other filters, or clear them to see every change.",
    () => changeFilters({}),
  );

  /**
   * Shows the cards that match the filters, most recently reported first (the server's order), after bringing each
   * card's quiet state up to date. The grid is only rebuilt when what it shows changes, so the clock's tick doesn't
   * replay a card's motion.
   */
  /** The cards changed: their choices are worked out again, and the bar redrawn at the next layout. */
  function cardsChanged(): void {
    choices = filterChoices([...shown.values()].map((entry) => entry.card));
    barStale = true;
  }

  function layout(): void {
    const at = now();
    const ordered = [...shown.values()].sort((a, b) => reportedAt(b.card) - reportedAt(a.card));
    const visible = ordered.filter(({ card, element }) =>
      matches(card, filters.filters, applyQuiet(element, card, at, quiet)),
    );

    const total = ordered.length;
    const hidden = total - visible.length;
    const noun = (n: number) => (n === 1 ? "change" : "changes");
    count.textContent =
      hidden > 0 ? `${visible.length} of ${total} ${noun(total)}` : `${total} ${noun(total)}`;
    count.title = hidden > 0 ? `${hidden} hidden by the filters` : "";

    const filtered = hasFilters(filters.filters) || filters.dropped.length > 0;
    if (total === 0 && !filtered) {
      content.replaceChildren(renderEmptyState());
      return;
    }

    if (barStale) {
      bar.update(filterOptions(choices, filters.filters), filters.filters, filters.dropped);
      barStale = false;
    }

    const body = visible.length === 0 ? noMatchBlock : grid;
    const elements = visible.map((entry) => entry.element);
    if (
      grid.children.length !== elements.length ||
      elements.some((element, index) => grid.children[index] !== element)
    ) {
      grid.replaceChildren(...elements);
    }

    if (content.firstElementChild !== bar.element || bar.element.nextElementSibling !== body) {
      content.replaceChildren(bar.element, body);
    }
  }

  const app: App = {
    render(cards) {
      const next = new Map<string, Shown>();
      for (const card of cards) {
        const current = shown.get(card.changeId);
        next.set(
          card.changeId,
          current && reportedAt(current.card) >= reportedAt(card)
            ? current
            : { card, element: renderCard(card, now(), quiet) },
        );
      }

      shown.clear();
      next.forEach((entry, id) => shown.set(id, entry));
      cardsChanged();
      loaded = true;
      layout();
    },

    upsert(card) {
      const current = shown.get(card.changeId);
      if (current && reportedAt(card) <= reportedAt(current.card)) {
        return false;
      }

      const element = renderCard(card, now(), quiet);
      if (current) {
        lightNewCells(current.element, element);
      }

      for (const entry of shown.values()) {
        entry.element.classList.remove("updated");
      }

      element.classList.add("updated");
      shown.set(card.changeId, { card, element });
      cardsChanged();
      if (loaded) {
        layout();
      }

      return true;
    },

    remove(changeId) {
      if (!shown.delete(changeId)) {
        return false;
      }

      cardsChanged();
      if (loaded) {
        layout();
      }

      return true;
    },

    leaveProject(projectId) {
      const left = projectId.toLowerCase();
      for (const [changeId, entry] of shown) {
        if (entry.card.project?.id.toLowerCase() === left) {
          shown.delete(changeId);
        }
      }

      cardsChanged();
      if (filters.filters.project === left) {
        const rest = { ...filters.filters };
        delete rest.project;
        changeFilters(rest);
      } else if (loaded) {
        layout();
      }
    },

    retagProject(change) {
      let changed = 0;
      for (const [changeId, entry] of shown) {
        const card = retagCard(entry.card, change);
        if (card !== entry.card) {
          const element = renderCard(card, now(), quiet);
          // Not a report: the card keeps its place and its "updated" mark, with no lighting.
          element.classList.toggle("updated", entry.element.classList.contains("updated"));
          shown.set(changeId, { card, element });
          changed++;
        }
      }

      if (changed > 0) {
        cardsChanged();
        if (loaded) {
          layout();
        }
      }

      return changed;
    },

    async refresh() {
      app.render(await fetchChanges(api));
    },

    updateTimes() {
      if (loaded) {
        layout();
      }
    },

    setFilters(next) {
      filters = next;
      barStale = true;
      if (loaded) {
        layout();
      }
    },

    setQuiet(settings) {
      quiet = settings;
      if (loaded) {
        layout();
      }
    },

    setStatus(next) {
      live.dataset["status"] = next;
      liveText.textContent =
        next === "live" ? "Live" : next === "offline" ? "Offline, reconnecting…" : "Connecting…";
    },
  };

  app.setStatus("connecting");
  try {
    await app.refresh();
  } catch (error) {
    content.replaceChildren(renderLoadError(error));
  }

  return app;
}

/**
 * The one orchestrated motion: cells that were unlit on the old card and are lit on the new one light in sequence.
 * Bars are matched by position (section, then whole change); a different section starts fresh, with no motion.
 */
function lightNewCells(before: HTMLElement, after: HTMLElement): void {
  const oldBars = [...before.querySelectorAll(".bar")];
  const newBars = [...after.querySelectorAll(".bar")];
  newBars.forEach((bar, index) => {
    const old = oldBars[index];
    if (
      !old ||
      old.querySelector(".bar-name")?.textContent !== bar.querySelector(".bar-name")?.textContent
    ) {
      return;
    }

    const wasLit = old.querySelectorAll(".cell.lit").length;
    const cells = [...bar.querySelectorAll<HTMLElement>(".cell.lit")];
    cells.slice(wasLit).forEach((cell, step) => {
      cell.classList.add("lighting");
      cell.style.animationDelay = `${step * LIGHTING_STEP_MS}ms`;
    });
  });
}

function renderEmptyState(): HTMLElement {
  const section = document.createElement("section");
  section.className = "empty-state";
  const heading = document.createElement("h2");
  heading.textContent = "No agents have reported yet";
  const text = document.createElement("p");
  text.textContent =
    "Connect one with the panopticon CLI, then ask it to report progress on a change.";
  const command = document.createElement("pre");
  command.textContent = [
    `panopticon login --server ${location.origin}`,
    "claude mcp add panopticon --scope local -- panopticon mcp",
  ].join("\n");
  const more = document.createElement("p");
  more.textContent = "Other agents and the full setup are in docs/cli.md.";
  section.append(heading, text, command, more);
  return section;
}

function renderLoadError(error: unknown): HTMLElement {
  const section = document.createElement("section");
  section.className = "load-error";
  section.setAttribute("role", "alert");
  const heading = document.createElement("h2");
  heading.textContent = "Couldn't load the changes";
  const text = document.createElement("p");
  text.textContent = `${error instanceof Error ? error.message : String(error)}. Check that the server is running, then reload.`;
  section.append(heading, text);
  return section;
}
