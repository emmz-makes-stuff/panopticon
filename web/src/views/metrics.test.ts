import { beforeEach, describe, expect, it } from "vitest";
import { signedIn, startTestShell } from "../testShell";
import { FakeEventSource, q, until } from "../testing";
import type { TeamMetrics, WeekMetric } from "../types";
import { weekLabel } from "./metrics";

const HOUR = 3600;

/** The 12 weeks ending with the week of 2 Nov 2026, three archives in the last two. */
const weeks = (): WeekMetric[] =>
  Array.from({ length: 12 }, (_, i) => {
    const weekStart = new Date(Date.UTC(2026, 7, 24 + 7 * i)).toISOString();
    if (i === 10) {
      return { weekStart, changes: 2, medianArchiveSeconds: 3.5 * HOUR };
    }

    return i === 11
      ? { weekStart, changes: 1, medianArchiveSeconds: 10 * HOUR }
      : { weekStart, changes: 0, medianArchiveSeconds: null };
  });

const TEAM: TeamMetrics = {
  changes: 3,
  medianArchiveSeconds: 5 * HOUR,
  medianSectionSeconds: 2 * HOUR,
  weeks: weeks(),
  slowestSections: [
    {
      changeId: "b",
      change: "beta",
      repo: { host: "github.com", owner: "emmz", name: "panopticon" },
      number: 1,
      title: "Server",
      durationSeconds: 3 * HOUR,
    },
    {
      changeId: "g",
      change: "gamma",
      repo: { host: "github.com", owner: "emmz", name: "meko" },
      number: 2,
      title: "Web",
      durationSeconds: 2 * HOUR,
    },
  ],
};

const NOTHING: TeamMetrics = {
  changes: 0,
  medianArchiveSeconds: null,
  medianSectionSeconds: null,
  weeks: weeks().map((w) => ({ ...w, changes: 0, medianArchiveSeconds: null })),
  slowestSections: [],
};

const CHOICES = {
  repos: [
    { host: "github.com", owner: "emmz", name: "meko" },
    { host: "github.com", owner: "emmz", name: "panopticon" },
  ],
  owners: [{ host: "github.com", owner: "emmz" }],
  members: [],
  machines: ["laptop", "vm-1"],
  clients: ["claude-code"],
  projects: [{ id: "6f1c2e4a-0000-4000-8000-000000000001", code: "PAN", name: "Panopticon" }],
  personal: true,
};

/** A metrics server answering by the query it's asked with. */
function metricsServer(answers: Record<string, TeamMetrics>) {
  return signedIn()
    .on("GET", "/api/history/filters", { status: 200, body: CHOICES })
    .on("GET", "/api/metrics", (call) => ({
      status: 200,
      body: answers[call.url.split("?")[1] ?? ""] ?? NOTHING,
    }));
}

const visible = (element: Element) => {
  const copy = element.cloneNode(true) as HTMLElement;
  copy.querySelectorAll(".visually-hidden").forEach((hidden) => hidden.remove());
  return copy.textContent;
};

const figures = (root: HTMLElement) =>
  [...root.querySelectorAll(".metrics-figure")].map((f) => [
    f.querySelector("dt")?.textContent,
    visible(f.querySelector("dd") as Element),
  ]);

const click = (link: Element | null | undefined) =>
  link?.dispatchEvent(new MouseEvent("click", { bubbles: true, button: 0 }));

beforeEach(() => {
  FakeEventSource.instances.length = 0;
});

// Spec change-metrics: Metrics page, Metrics page filters, Metrics link.
describe("metrics page", () => {
  it("Team metrics: the count, the medians, the weekly chart and the slowest sections linking to their changes", async () => {
    const { root, heading } = await startTestShell("/metrics", metricsServer({ "": TEAM }));
    await until(() => root.querySelector(".metrics-figures") !== null);

    expect(heading()).toBe("Metrics");
    expect(figures(root)).toEqual([
      ["Changes archived", "3"],
      ["Median time to archive", "5h"],
      ["Median time per section", "2h"],
    ]);

    const chart = q(root, ".metrics-weeks svg");
    expect(chart.getAttribute("role")).toBe("img");
    expect(chart.getAttribute("aria-label")).toBe(
      "Median time to archive by week: 2 of 12 with a value, the largest 10h (9 Nov).",
    );
    expect(chart.querySelectorAll("rect.chart-bar")).toHaveLength(2);
    const rows = [...root.querySelectorAll(".metrics-weeks tbody tr")].map((r) => r.textContent);
    expect(rows).toHaveLength(12);
    expect(rows[0]).toBe("24 AugNo archives");
    expect(rows.slice(10)).toEqual(["2 Nov3h 30m", "9 Nov10h"]);

    const slowest = [...root.querySelectorAll(".metrics-slow")];
    expect(slowest.map(visible)).toEqual([
      "beta emmz/panopticonSection 1: Server3h",
      "gamma emmz/mekoSection 2: Web2h",
    ]);
    expect(
      [...root.querySelectorAll<HTMLAnchorElement>(".metrics-slow a")].map((a) =>
        a.getAttribute("href"),
      ),
    ).toEqual(["/changes/b", "/changes/g"]);
  });

  it("a slowest section opens its change page", async () => {
    const server = metricsServer({ "": TEAM }).on("GET", "/api/changes/b", {
      status: 404,
      body: { detail: "Not found." },
    });
    const { root, nav, heading } = await startTestShell("/metrics", server);
    await until(() => root.querySelector(".metrics-slow a") !== null);

    click(root.querySelector(".metrics-slow a"));
    await until(() => heading() === "Change not found");

    expect(nav.path()).toBe("/changes/b");
  });

  it("Filtered metrics: the address's filters are asked for and shown in the bar", async () => {
    const panopticon = "repo=github.com%2Femmz%2Fpanopticon";
    const server = metricsServer({ [panopticon]: { ...TEAM, changes: 2 } });
    const { root } = await startTestShell(`/metrics?${panopticon}`, server);
    await until(() => root.querySelector(".metrics-figures") !== null);

    expect(server.to("GET", "/api/metrics").map((c) => c.url)).toEqual([
      `/api/metrics?${panopticon}`,
    ]);
    expect(figures(root)[0]).toEqual(["Changes archived", "2"]);
    expect(q<HTMLSelectElement>(root, "select[name=repo]").value).toBe(
      "github.com/emmz/panopticon",
    );
    expect(root.querySelector("select[name=status]")).toBeNull();
  });

  it("a filter change loads the numbers again in place, writing the address", async () => {
    const server = metricsServer({ "": TEAM, "machine=laptop": { ...TEAM, changes: 1 } });
    const { root, nav } = await startTestShell("/metrics", server);
    await until(() => root.querySelector(".metrics-figures") !== null);
    const heading = q(root, ".view h2");

    const select = q<HTMLSelectElement>(root, "select[name=machine]");
    select.value = "laptop";
    select.dispatchEvent(new Event("change"));
    await until(() => figures(root)[0]?.[1] === "1");

    expect(nav.path()).toBe("/metrics?machine=laptop");
    expect(server.to("GET", "/api/metrics").map((c) => c.url)).toEqual([
      "/api/metrics",
      "/api/metrics?machine=laptop",
    ]);
    expect(q(root, ".view h2")).toBe(heading);
    expect(server.to("GET", "/api/history/filters")).toHaveLength(1);
  });

  it("Nothing matches: says no archived changes match, with a way to clear the filters", async () => {
    const { root, nav } = await startTestShell("/metrics?machine=nowhere", metricsServer({}));
    await until(() => root.querySelector(".no-match") !== null);

    expect(q(root, ".no-match h2").textContent).toBe("No archived changes match these filters");
    expect(root.querySelector(".metrics-figures")).toBeNull();
    q<HTMLButtonElement>(root, ".no-match button").click();
    await until(() => nav.path() === "/metrics");

    expect(nav.path()).toBe("/metrics");
  });

  it("nothing archived yet says metrics will appear", async () => {
    const { root } = await startTestShell("/metrics", metricsServer({}));
    await until(() => root.querySelector(".metrics-empty-state") !== null);

    expect(q(root, ".metrics-empty-state").textContent).toContain(
      "No changes have been archived yet.",
    );
    expect(root.querySelector(".no-match")).toBeNull();
  });

  it("a failed load says so and offers to try again", async () => {
    let failing = true;
    const server = metricsServer({}).on("GET", "/api/metrics", () =>
      failing
        ? { status: 500, body: { detail: "The database is down." } }
        : { status: 200, body: TEAM },
    );
    const { root } = await startTestShell("/metrics", server);
    await until(
      () => root.querySelector(".view .form-error")?.textContent === "The database is down.",
    );

    expect(q(root, ".view .form-error").textContent).toBe("The database is down.");
    const retry = q<HTMLButtonElement>(root, ".show-more");
    expect(retry.hidden).toBe(false);

    failing = false;
    retry.click();
    await until(() => root.querySelector(".metrics-figures") !== null);

    expect(retry.hidden).toBe(true);
    expect(q(root, ".view .form-error").textContent).toBe("");
  });

  it("Go to metrics: the header's Metrics link opens the metrics page", async () => {
    const { root, nav, heading } = await startTestShell("/", metricsServer({ "": TEAM }));

    click([...root.querySelectorAll("nav.account a")].find((a) => a.textContent === "Metrics"));
    await until(() => heading() === "Metrics");

    expect(nav.path()).toBe("/metrics");
  });

  it("labels weeks by their Monday in UTC", () => {
    expect(weekLabel("2026-11-02T00:00:00+00:00")).toBe("2 Nov");
  });
});

// Spec change-metrics (projects): "One Project's metrics".
describe("metrics Project filter", () => {
  it("One Project's metrics: the address's Project is asked for, and shown in the bar", async () => {
    const PAN_ID = "6f1c2e4a-0000-4000-8000-000000000001";
    const server = metricsServer({ [`project=${PAN_ID}`]: { ...TEAM, changes: 3 } });
    const { root } = await startTestShell(`/metrics?project=${PAN_ID}`, server);
    await until(() => root.querySelector(".metrics-figures") !== null);

    expect(server.to("GET", "/api/metrics").map((c) => c.url)).toEqual([
      `/api/metrics?project=${PAN_ID}`,
    ]);
    expect(figures(root)[0]).toEqual(["Changes archived", "3"]);
    expect(q<HTMLSelectElement>(root, "select[name=project]").value).toBe(PAN_ID);
  });
});
