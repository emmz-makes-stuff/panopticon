import { ApiError, fetchHistoryChoices, fetchMetrics } from "../api";
import { renderBarChart } from "../charts";
import { alertLine, button, el, page, routeLink } from "../dom";
import { createFilterBar, noMatch } from "../filterBar";
import {
  filterOptions,
  hasFilters,
  parseFilters,
  toQuery,
  type FilterChoices,
  type Filters,
} from "../filters";
import { calendarDate, formatDuration } from "../format";
import { changeHref } from "../links";
import { durationRule, durationText } from "../metricsPanel";
import type { View } from "../router";
import type { Env } from "../shell";
import type { SlowSection, TeamMetrics } from "../types";
import { HISTORY_KEYS } from "./history";

const NO_CHOICES: FilterChoices = { repos: [], owners: [], members: [], machines: [], clients: [] };

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/** A week's label on the chart and in its table, e.g. "2 Nov" (UTC, as the weeks are). */
export function weekLabel(iso: string): string {
  return calendarDate(iso, { year: false });
}

/**
 * `/metrics` (change-metrics D5; spec: Metrics page, Metrics page filters): the team's numbers over archived changes,
 * filtered like the history. The filters come from the address; a choice in the bar writes it, and the router calls
 * `update`, which loads the numbers again in place.
 */
export function metricsView(env: Env): View {
  return async ({ root, params, navigate }) => {
    let parsed = parseFilters(params);
    let choices = NO_CHOICES;
    const changeFilters = (filters: Filters) => {
      const query = toQuery(filters, HISTORY_KEYS);
      navigate(query ? `/metrics?${query}` : "/metrics");
    };
    const bar = createFilterBar({ keys: HISTORY_KEYS, onChange: changeFilters });
    const showBar = () =>
      bar.update(filterOptions(choices, parsed.filters), parsed.filters, parsed.dropped);

    const body = el("div", "metrics-body");
    const error = alertLine();
    const retry = button("Try again", () => void load(), "show-more");
    retry.hidden = true;
    /** Bumped on each load and when the view is left, so an older answer is dropped. */
    let generation = 0;

    async function load(): Promise<void> {
      const ticket = ++generation;
      retry.hidden = true;
      error.clear();
      try {
        const metrics = await fetchMetrics(env.api, toQuery(parsed.filters, HISTORY_KEYS));
        if (ticket === generation) {
          body.replaceChildren(...render(metrics));
        }
      } catch (failure) {
        if (ticket !== generation || (failure instanceof ApiError && failure.status === 401)) {
          return;
        }

        body.replaceChildren();
        error.show(failureText(failure));
        retry.hidden = false;
      }
    }

    function render(metrics: TeamMetrics): Node[] {
      if (metrics.changes === 0) {
        return [empty(hasFilters(parsed.filters, HISTORY_KEYS), () => changeFilters({}))];
      }

      return [
        figures(metrics),
        el(
          "section",
          "metrics-weeks",
          el("h3", undefined, "Median time to archive by week"),
          el("p", "metrics-note", "The last 12 weeks, by archive date. Weeks start on Monday."),
          renderBarChart({
            title: "Median time to archive by week",
            bars: metrics.weeks.map((week) => ({
              label: weekLabel(week.weekStart),
              value: week.medianArchiveSeconds,
            })),
            formatValue: formatDuration,
            columns: ["Week of", "Median time to archive"],
            noValue: "No archives",
          }),
        ),
        slowest(metrics.slowestSections),
        el("p", "metrics-note", "Wall-clock time: from a change's first report to its archive."),
      ];
    }

    showBar();
    const metricsPage = page("Metrics", bar.element, error.element, body, retry);
    metricsPage.classList.add("metrics-page");
    root.replaceChildren(metricsPage);
    // The choices load beside the numbers; without them the page still shows, and the bar offers only what's chosen.
    const loadingChoices = fetchHistoryChoices(env.api).then(
      (loaded) => {
        choices = loaded;
        showBar();
      },
      (failure: unknown) => {
        if (!(failure instanceof ApiError && failure.status === 401)) {
          console.warn("Couldn't load the metrics' filter choices.", failure);
        }
      },
    );
    await Promise.all([load(), loadingChoices]);
    return {
      teardown: () => {
        generation++;
      },
      update: (next) => {
        parsed = parseFilters(next);
        showBar();
        void load();
      },
    };
  };
}

function figures(metrics: TeamMetrics): HTMLElement {
  const figure = (term: string, value: Node | string) =>
    el("div", "metrics-figure", el("dt", undefined, term), el("dd", undefined, value));
  const median = (seconds: number | null, none: string) =>
    seconds === null ? none : durationText(seconds);
  return el(
    "dl",
    "metrics-figures",
    figure("Changes archived", String(metrics.changes)),
    figure("Median time to archive", median(metrics.medianArchiveSeconds, "None")),
    figure("Median time per section", median(metrics.medianSectionSeconds, "No finished sections")),
  );
}

function slowest(sections: readonly SlowSection[]): HTMLElement {
  const section = el("section", "metrics-slowest", el("h3", undefined, "Slowest sections"));
  if (sections.length === 0) {
    section.append(el("p", "metrics-empty", "No finished sections yet."));
    return section;
  }

  const list = el("ol", "metrics-slowest-list");
  const longest = sections[0]?.durationSeconds ?? 0;
  for (const slow of sections) {
    list.append(
      el(
        "li",
        "metrics-slow",
        el(
          "span",
          "metrics-slow-change",
          routeLink(changeHref(slow.changeId), slow.change, "metrics-slow-link"),
          " ",
          el("span", "metrics-slow-repo", `${slow.repo.owner}/${slow.repo.name}`),
        ),
        el("span", "metrics-slow-section", `Section ${slow.number}: ${slow.title}`),
        durationRule(slow.durationSeconds, longest),
        durationText(slow.durationSeconds, "duration metrics-slow-time"),
      ),
    );
  }

  section.append(list);
  return section;
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
    "metrics-empty-state",
    el("p", undefined, "No changes have been archived yet."),
    el("p", undefined, "Metrics appear here once a change is archived with /opsx:archive."),
  );
}
