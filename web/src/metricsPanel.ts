import { el } from "./dom";
import { formatDuration, formatDurationLong } from "./format";
import type { ChangeMetrics, SectionMetric } from "./types";

/**
 * A duration as shown: the short form ("5d 6h") on screen, the long form ("5 days 6 hours") as its tooltip and as the
 * text a screen reader reads instead.
 */
export function durationText(seconds: number, className = "duration"): HTMLElement {
  const long = formatDurationLong(seconds);
  const shown = el("span", undefined, formatDuration(seconds));
  shown.setAttribute("aria-hidden", "true");
  const span = el("span", className, shown, el("span", "visually-hidden", long));
  span.title = long;
  return span;
}

/**
 * The change page's metrics (change-metrics D5; spec: Change page metrics): "Took …" or "Running for …", then each
 * measured section in number order, a section in progress marked with its time so far.
 */
export function renderChangeMetrics(metrics: ChangeMetrics): Node[] {
  const total = el(
    "p",
    "metrics-total",
    metrics.active ? "Running for " : "Took ",
    durationText(metrics.durationSeconds, "duration metrics-total-duration"),
  );
  const note = el(
    "p",
    "metrics-note",
    metrics.active
      ? "Wall-clock time since the first report."
      : "Wall-clock time, first report to archive.",
  );

  if (metrics.sections.length === 0) {
    return [total, note, el("p", "metrics-empty", "No section has been started yet.")];
  }

  const list = el("ol", "metrics-sections");
  list.setAttribute("aria-label", "Time per section");
  for (const section of metrics.sections) {
    list.append(sectionRow(section));
  }

  return [total, note, list];
}

function sectionRow(section: SectionMetric): HTMLElement {
  const row = el(
    "li",
    section.inProgress ? "metrics-section in-progress" : "metrics-section",
    el("span", "metrics-section-number", String(section.number)),
    el("span", "metrics-section-title", section.title),
  );
  row.append(
    section.inProgress
      ? el(
          "span",
          "metrics-section-time",
          el("span", "metrics-in-progress", "in progress"),
          " · ",
          durationText(section.durationSeconds),
          " so far",
        )
      : el("span", "metrics-section-time", durationText(section.durationSeconds)),
  );
  return row;
}
