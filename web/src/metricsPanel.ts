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
  const longest = Math.max(...metrics.sections.map((s) => s.durationSeconds));
  for (const section of metrics.sections) {
    list.append(sectionRow(section, longest, metrics.active));
  }

  return [total, note, list];
}

/**
 * A duration rule (web/DESIGN.md, "Metrics"): a thin line as long as this time is against the longest beside it, so
 * a slow one stands out. Dashed while still running. Decorative: the time is always printed beside it.
 */
export function durationRule(seconds: number, longest: number, running = false): HTMLElement {
  const rule = el("span", running ? "duration-rule running" : "duration-rule");
  rule.setAttribute("aria-hidden", "true");
  const share = longest > 0 ? Math.max(2, Math.round((seconds / longest) * 100)) : 0;
  rule.style.setProperty("--share", `${share}%`);
  return rule;
}

/**
 * A section's row. One not finished is "in progress, 1h so far" while the change is active, and "unfinished, 3h" once
 * it's archived, since its clock stopped at the archive (spec: A section unfinished at archive).
 */
function sectionRow(section: SectionMetric, longest: number, active: boolean): HTMLElement {
  const state = !section.inProgress ? undefined : active ? "in-progress" : "unfinished";
  const row = el(
    "li",
    state ? `metrics-section ${state}` : "metrics-section",
    el("span", "metrics-section-number", String(section.number)),
    el("span", "metrics-section-title", section.title),
    durationRule(section.durationSeconds, longest, section.inProgress),
  );
  // The state and the time joined by a comma, not a middot (DESIGN.md principle 5).
  const time = el("span", "metrics-section-time");
  if (state === "in-progress") {
    time.append(
      el("span", "metrics-state", "in progress"),
      ", ",
      durationText(section.durationSeconds),
      " so far",
    );
  } else if (state === "unfinished") {
    time.append(
      el("span", "metrics-state", "unfinished"),
      ", ",
      durationText(section.durationSeconds),
    );
  } else {
    time.append(durationText(section.durationSeconds));
  }

  row.append(time);
  return row;
}
