import { describe, expect, it } from "vitest";
import {
  barHeights,
  chartSummary,
  PLOT_HEIGHT,
  renderBarChart,
  type BarChartOptions,
} from "./charts";
import { formatDuration } from "./format";
import { q } from "./testing";

const options = (bars: BarChartOptions["bars"]): BarChartOptions => ({
  title: "Median time to archive by week",
  bars,
  formatValue: formatDuration,
  columns: ["Week of", "Median time to archive"],
  noValue: "No archives",
});

const WEEKS = [
  { label: "26 Oct", value: null },
  { label: "2 Nov", value: 3 * 3600 },
  { label: "9 Nov", value: 12 * 3600 },
];

// change-metrics D5: hand-built SVG, role="img" with a text summary, and a hidden table of values.
describe("bar chart", () => {
  it("draws bars relative to the largest, none for a missing value", () => {
    expect(barHeights(WEEKS)).toEqual([0, PLOT_HEIGHT / 4, PLOT_HEIGHT]);
    expect(
      barHeights([
        { label: "a", value: 0 },
        { label: "b", value: null },
      ]),
    ).toEqual([0, 0]);
    // A tiny value still shows.
    expect(
      barHeights([
        { label: "a", value: 1 },
        { label: "b", value: 10_000 },
      ])[0],
    ).toBe(1);
  });

  it("is an image named by its summary", () => {
    const figure = renderBarChart(options(WEEKS));
    const svg = q(figure, "svg");

    expect(svg.getAttribute("role")).toBe("img");
    expect(svg.getAttribute("aria-label")).toBe(
      "Median time to archive by week: 2 of 3 with a value, the largest 12h (9 Nov).",
    );
    expect(
      [...figure.querySelectorAll("rect.chart-bar")].map((r) => r.getAttribute("height")),
    ).toEqual([String(PLOT_HEIGHT / 4), String(PLOT_HEIGHT)]);
    expect([...figure.querySelectorAll(".chart-label")].map((t) => t.textContent)).toEqual([
      "26 Oct",
      "2 Nov",
      "9 Nov",
    ]);
    expect(q(figure, ".chart-scale").textContent).toBe("12h");
  });

  it("says when there's nothing to show", () => {
    expect(chartSummary(options([{ label: "2 Nov", value: null }]))).toBe(
      "Median time to archive by week: no values to show.",
    );
    expect(
      renderBarChart(options([{ label: "2 Nov", value: null }])).querySelector("rect"),
    ).toBeNull();
  });

  it("gives every value as text in a hidden table", () => {
    const table = q(renderBarChart(options(WEEKS)), "table");

    expect(table.classList.contains("visually-hidden")).toBe(true);
    expect(q(table, "caption").textContent).toBe("Median time to archive by week");
    expect([...table.querySelectorAll("thead th")].map((th) => th.textContent)).toEqual([
      "Week of",
      "Median time to archive",
    ]);
    expect(
      [...table.querySelectorAll("tbody tr")].map((row) =>
        [...row.children].map((cell) => cell.textContent),
      ),
    ).toEqual([
      ["26 Oct", "No archives"],
      ["2 Nov", "3h"],
      ["9 Nov", "12h"],
    ]);
  });
});
