import { fetchChanges, type FetchLike } from "./api";
import { renderCard } from "./card";
import { relativeTime } from "./format";
import type { ChangeCard } from "./types";

/** The product's name, exactly as written (web/DESIGN.md, "Name"). */
export const PRODUCT_NAME = "a PANOPTICON";

/** Gap between consecutive cells lighting up after a report (web/DESIGN.md, principle 4). */
export const LIGHTING_STEP_MS = 60;

export type LiveStatus = "connecting" | "live" | "offline";

export interface AppDeps {
  fetch?: FetchLike;
  now?: () => Date;
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
  /** Loads every card from the server and renders them. Throws if the load fails, leaving the cards as they were. */
  refresh(): Promise<void>;
  /** Shows whether the live connection is up. */
  setStatus(status: LiveStatus): void;
  /** Re-reads every card's "last reported" time against the clock (the main entry calls it every 30 s). */
  updateTimes(): void;
}

interface Shown {
  card: ChangeCard;
  element: HTMLElement;
}

const reportedAt = (card: ChangeCard) => Date.parse(card.lastReportedAt);

/** Mounts the dashboard into `root`, then loads the cards. Resolves once the first load has rendered (or failed). */
export async function mountApp(root: HTMLElement, deps: AppDeps = {}): Promise<App> {
  const now = deps.now ?? (() => new Date());
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

  const content = document.createElement("div");
  content.className = "content";
  const grid = document.createElement("section");
  grid.className = "grid";
  grid.setAttribute("aria-label", "Changes");
  root.replaceChildren(header, content);

  // Most recently reported first, as the server orders them.
  function layout(): void {
    const ordered = [...shown.values()].sort((a, b) => reportedAt(b.card) - reportedAt(a.card));
    count.textContent = ordered.length === 1 ? "1 change" : `${ordered.length} changes`;
    if (ordered.length === 0) {
      content.replaceChildren(renderEmptyState());
      return;
    }

    grid.replaceChildren(...ordered.map((entry) => entry.element));
    if (grid.parentElement !== content) {
      content.replaceChildren(grid);
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
            : { card, element: renderCard(card, now()) },
        );
      }

      shown.clear();
      next.forEach((entry, id) => shown.set(id, entry));
      layout();
    },

    upsert(card) {
      const current = shown.get(card.changeId);
      if (current && reportedAt(card) <= reportedAt(current.card)) {
        return false;
      }

      const element = renderCard(card, now());
      if (current) {
        lightNewCells(current.element, element);
      }

      for (const entry of shown.values()) {
        entry.element.classList.remove("updated");
      }

      element.classList.add("updated");
      shown.set(card.changeId, { card, element });
      layout();
      return true;
    },

    async refresh() {
      app.render(await fetchChanges(deps.fetch));
    },

    updateTimes() {
      for (const { card, element } of shown.values()) {
        const time = element.querySelector("time");
        if (time) {
          time.textContent = relativeTime(card.lastReportedAt, now());
        }
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
    "claude mcp add panopticon --scope local \\",
    `  --env PANOPTICON_SERVER_URL=${location.origin} \\`,
    '  --env PANOPTICON_TOKEN="$PANOPTICON_TOKEN" \\',
    "  -- panopticon mcp",
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
