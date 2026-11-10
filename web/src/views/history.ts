import { leadingProjectLabel } from "../projectLabel";
import { tagChips } from "../tagChips";
import { ApiError, fetchHistory, fetchHistoryChoices } from "../api";
import { alertLine, button, el, page, routeLink } from "../dom";
import { createFilterBar, noMatch } from "../filterBar";
import {
  filterOptions,
  hasFilters,
  parseFilters,
  toQuery,
  type FilterChoices,
  type FilterKey,
  type Filters,
} from "../filters";
import { agentName, calendarDate } from "../format";
import type { View } from "../router";
import type { Env } from "../shell";
import type { HistoryEntry } from "../types";
import { changeHref } from "../links";

/** The history's filters: every dimension but status, since every change here is archived (spec change-filters). */
export const HISTORY_KEYS: readonly FilterKey[] = [
  "project",
  "repo",
  "owner",
  "member",
  "machine",
  "client",
];

const NO_CHOICES: FilterChoices = {
  repos: [],
  owners: [],
  members: [],
  machines: [],
  clients: [],
  projects: [],
  personal: false,
};

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/**
 * `/history` (spec change-history, design D11): archived changes, the most recently archived first, each opening its
 * change page at the final snapshot. "Show more" fetches the next page.
 *
 * Filters (spec change-filters, filters-and-idle-cards D3–D5) come from the address and are applied by the server, so
 * each page holds up to 50 matches and "Show more" continues within them. A choice in the bar writes the address; the
 * router calls `update`, which starts the list again in place. The choices are the server's, among archived changes.
 */
export function historyView(env: Env): View {
  return async ({ root, params, navigate }) => {
    let parsed = parseFilters(params);
    let choices = NO_CHOICES;
    const changeFilters = (filters: Filters) => {
      const query = toQuery(filters, HISTORY_KEYS);
      navigate(query ? `/history?${query}` : "/history");
    };
    const bar = createFilterBar({ keys: HISTORY_KEYS, onChange: changeFilters });
    const showBar = () =>
      bar.update(filterOptions(choices, parsed.filters), parsed.filters, parsed.dropped);

    const body = el("div", "history-body");
    const error = alertLine();
    const more = button("Show more", () => void load(), "show-more");
    let list = el("ol", "history-list");
    let before: string | null = null;
    /** Bumped when the list starts again (new filters) or the view is left, so an older answer is dropped. */
    let generation = 0;

    async function load(): Promise<void> {
      const ticket = generation;
      more.disabled = true;
      try {
        const next = await fetchHistory(env.api, before, toQuery(parsed.filters, HISTORY_KEYS));
        if (ticket !== generation) {
          return;
        }

        if (!before && next.changes.length === 0) {
          body.replaceChildren(
            empty(hasFilters(parsed.filters, HISTORY_KEYS), () => changeFilters({})),
          );
        }

        list.append(...next.changes.map(row));
        before = next.nextBefore;
        more.hidden = before === null;
        more.textContent = "Show more";
        error.clear();
      } catch (failure) {
        if (ticket !== generation) {
          return;
        }

        if (!(failure instanceof ApiError && failure.status === 401)) {
          error.show(failureText(failure));
          // A failed first load leaves nothing to show more of, so the button offers to try again.
          if (before === null) {
            more.textContent = "Try again";
            more.hidden = false;
          }
        }
      } finally {
        if (ticket === generation) {
          more.disabled = false;
        }
      }
    }

    function restart(): Promise<void> {
      generation++;
      before = null;
      list = el("ol", "history-list");
      body.replaceChildren(list);
      more.hidden = true;
      error.clear();
      return load();
    }

    showBar();
    root.replaceChildren(page("History", bar.element, error.element, body, more));
    // The choices load beside the first page; without them the list still shows, and the bar offers only what's chosen.
    const loadingChoices = fetchHistoryChoices(env.api).then(
      (loaded) => {
        choices = loaded;
        showBar();
      },
      (failure: unknown) => {
        if (!(failure instanceof ApiError && failure.status === 401)) {
          console.warn("Couldn't load the history's filter choices.", failure);
        }
      },
    );
    await Promise.all([restart(), loadingChoices]);
    return {
      teardown: () => {
        generation++;
      },
      update: (next) => {
        parsed = parseFilters(next);
        showBar();
        void restart();
      },
    };
  };
}

function row(entry: HistoryEntry): HTMLLIElement {
  // A retired copy says where its work went, in place of an archive folder (spec change-history, "History list").
  const archived = el(
    "time",
    "history-date",
    entry.linkedTo
      ? `Linked to ${entry.linkedTo.code} on ${calendarDate(entry.archivedAt)}`
      : `Archived ${calendarDate(entry.archivedAt)}`,
  );
  archived.dateTime = entry.archivedAt;
  const agents = el("ul", "card-agents history-agents");
  agents.setAttribute("aria-label", "Agents");
  for (const agent of entry.agents) {
    const item = el("li", "card-agent", agentName(agent));
    if (agent.member) {
      item.append(" ", el("span", "card-agent-member", agent.member));
    }

    agents.append(item);
  }

  return el(
    "li",
    "history-item",
    el("h3", "history-change", routeLink(changeHref(entry.changeId), entry.change, "history-link")),
    el(
      "p",
      "history-meta",
      ...leadingProjectLabel(entry),
      el("span", "history-repo", `${entry.repo.host} ${entry.repo.owner}/${entry.repo.name}`),
      " ",
      archived,
    ),
    tagChips(entry),
    agents,
  );
}

function empty(filtered: boolean, clear: () => void): HTMLElement {
  if (filtered) {
    return noMatch(
      "No archived changes match these filters",
      "Choose other filters, or clear them to see every archived change.",
      clear,
    );
  }

  return el(
    "div",
    "history-empty",
    el("p", undefined, "No changes have been archived yet."),
    el(
      "p",
      undefined,
      "When a change is archived with /opsx:archive, it leaves the dashboard and appears here, with its final documents.",
    ),
  );
}
