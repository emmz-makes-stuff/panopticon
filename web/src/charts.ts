import { el } from "./dom";

const SVG = "http://www.w3.org/2000/svg";

/** One bar: its axis label, and its value (null for none, drawn as no bar). */
export interface Bar {
  label: string;
  value: number | null;
}

export interface BarChartOptions {
  /** What the chart shows, e.g. "Median time to archive by week". The table's caption and the summary's start. */
  title: string;
  bars: readonly Bar[];
  /** A value as text, for the summary, the table and the top of the axis. */
  formatValue: (value: number) => string;
  /** The table's column headings: the bars' label, then their value. */
  columns: readonly [string, string];
  /** What a bar with no value reads as in the table, e.g. "No archives". */
  noValue: string;
}

/** The plot's size in viewBox units: each bar gets one slot, and the axis labels sit under the plot. */
export const PLOT_HEIGHT = 100;
const SLOT = 24;
const BAR = 16;
const LABEL_SPACE = 18;
const TOP_SPACE = 14;

/** Each bar's height in the plot, relative to the largest value; a null or zero value has none. */
export function barHeights(bars: readonly Bar[]): number[] {
  const largest = Math.max(0, ...bars.map((bar) => bar.value ?? 0));
  return bars.map((bar) =>
    largest > 0 && bar.value ? Math.max(1, (bar.value / largest) * PLOT_HEIGHT) : 0,
  );
}

/** One sentence for screen readers: how many bars have values, and the largest. */
export function chartSummary(options: BarChartOptions): string {
  const valued = options.bars.filter((bar) => bar.value !== null);
  if (valued.length === 0) {
    return `${options.title}: no values to show.`;
  }

  const largest = valued.reduce((a, b) => ((b.value ?? 0) > (a.value ?? 0) ? b : a));
  return `${options.title}: ${valued.length} of ${options.bars.length} with a value, the largest ${options.formatValue(largest.value ?? 0)} (${largest.label}).`;
}

/**
 * A small inline SVG bar chart (change-metrics D5): bars in the lit colour on a baseline, with each bar's label under
 * it and the largest value at the top. The SVG is `role="img"` with the summary as its name, and a visually hidden
 * table gives every value as text.
 */
export function renderBarChart(options: BarChartOptions): HTMLElement {
  const width = options.bars.length * SLOT;
  const height = TOP_SPACE + PLOT_HEIGHT + LABEL_SPACE;
  const baseline = TOP_SPACE + PLOT_HEIGHT;

  const svg = svgEl("svg", "chart-svg", {
    viewBox: `0 0 ${width} ${height}`,
    role: "img",
    "aria-label": chartSummary(options),
  });

  const largest = Math.max(0, ...options.bars.map((bar) => bar.value ?? 0));
  if (largest > 0) {
    svg.append(svgText("chart-scale", 0, TOP_SPACE - 4, options.formatValue(largest), "start"));
  }

  const heights = barHeights(options.bars);
  options.bars.forEach((bar, index) => {
    const x = index * SLOT + (SLOT - BAR) / 2;
    const barHeight = heights[index] ?? 0;
    if (barHeight > 0) {
      const rect = svgEl("rect", "chart-bar", {
        x: String(x),
        y: String(baseline - barHeight),
        width: String(BAR),
        height: String(barHeight),
        rx: "2",
      });
      rect.dataset["value"] = String(bar.value);
      svg.append(rect);
    }

    svg.append(svgText("chart-label", index * SLOT + SLOT / 2, height - 4, bar.label, "middle"));
  });
  svg.append(
    svgEl("line", "chart-axis", {
      x1: "0",
      x2: String(width),
      y1: String(baseline),
      y2: String(baseline),
    }),
  );

  const table = el(
    "table",
    "visually-hidden chart-table",
    el("caption", undefined, options.title),
    el(
      "thead",
      undefined,
      el(
        "tr",
        undefined,
        el("th", undefined, options.columns[0]),
        el("th", undefined, options.columns[1]),
      ),
    ),
  );
  const body = el("tbody");
  for (const bar of options.bars) {
    const label = el("th", undefined, bar.label);
    label.scope = "row";
    body.append(
      el(
        "tr",
        undefined,
        label,
        el("td", undefined, bar.value === null ? options.noValue : options.formatValue(bar.value)),
      ),
    );
  }

  table.append(body);
  return el("figure", "chart", svg, table);
}

function svgEl<K extends keyof SVGElementTagNameMap>(
  tag: K,
  className: string,
  attributes: Record<string, string>,
): SVGElementTagNameMap[K] {
  const element = document.createElementNS(SVG, tag);
  element.setAttribute("class", className);
  for (const [name, value] of Object.entries(attributes)) {
    element.setAttribute(name, value);
  }

  return element;
}

function svgText(
  className: string,
  x: number,
  y: number,
  text: string,
  anchor: "start" | "middle",
): SVGTextElement {
  const element = svgEl("text", className, {
    x: String(x),
    y: String(y),
    "text-anchor": anchor,
  });
  element.textContent = text;
  return element;
}
