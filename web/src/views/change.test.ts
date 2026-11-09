import { beforeEach, describe, expect, it } from "vitest";
import { CHANGE_ARCHIVED, CHANGE_UPDATED } from "../live";
import { savedReturn, signedIn, signedOut, startTestShell } from "../testShell";
import { card, FakeEventSource, q, until } from "../testing";
import type { ChangeMetrics, ChangePage, DocumentEntry, SectionMetric } from "../types";
import { changeHref } from "../links";
import { defaultDocument } from "./change";

const doc = (path: string, kind: DocumentEntry["kind"], title: string): DocumentEntry => ({
  path,
  kind,
  title,
  sizeBytes: 10,
});

const TEAM_AUTH: DocumentEntry[] = [
  doc("proposal.md", "proposal", "Proposal"),
  doc("design.md", "design", "Design"),
  doc("tasks.md", "tasks", "Tasks"),
  doc("specs/cli-bridge/spec.md", "spec", "cli-bridge"),
  doc("specs/live-dashboard/spec.md", "spec", "live-dashboard"),
  doc("devlog/20261108-1645-stopped.md", "devlog", "stopped · 2026-11-08 16:45"),
  doc("devlog/20261108-1200-decision.md", "devlog", "decision · 2026-11-08 12:00"),
  doc("devlog/20261108-0910-done.md", "devlog", "done · 2026-11-08 09:10"),
];

const changePage = (overrides: Partial<ChangePage> = {}): ChangePage => ({
  card: card({ changeId: "c1", change: "team-auth" }),
  archivedAt: null,
  archiveFolder: null,
  documents: TEAM_AUTH,
  ...overrides,
});

const html = (path: string) => ({ status: 200, body: { path, html: `<h1>${path}</h1>` } });

function serverFor(page: ChangePage = changePage()) {
  const server = signedIn().on("GET", "/api/changes/c1", { status: 200, body: page });
  for (const document of page.documents) {
    server.on("GET", `/api/changes/c1/documents/${document.path}`, html(document.path));
  }

  return server;
}

const links = (root: HTMLElement) =>
  [...root.querySelectorAll<HTMLAnchorElement>(".doc-nav a")].map((a) => a.textContent);
const shownPath = (root: HTMLElement) =>
  root.querySelector<HTMLElement>("article.doc")?.dataset["path"];

beforeEach(() => {
  FakeEventSource.instances.length = 0;
});

// Spec change-documents: Document list, Rendered documents, Documents need a session, Live drill-down.
describe("change page", () => {
  it("Open an active change: lists proposal, design, tasks, specs, then devlog newest first, showing the proposal", async () => {
    const server = serverFor();
    const { root, heading } = await startTestShell("/changes/c1", server);
    await until(() => shownPath(root) === "proposal.md");

    expect(heading()).toBe("team-auth");
    expect(links(root)).toEqual([
      "Proposal",
      "Design",
      "Tasks",
      "cli-bridge",
      "live-dashboard",
      "stopped · 2026-11-08 16:45",
      "decision · 2026-11-08 12:00",
      "done · 2026-11-08 09:10",
    ]);
    expect(q(root, "article.doc h1").textContent).toBe("proposal.md");
    expect(q(root, '.doc-nav a[aria-current="page"]').textContent).toBe("Proposal");
    expect(q(root, ".change-repo").textContent).toBe("github.com emmz/panopticon");
  });

  it("selecting a document opens it by ?doc=, which a reload keeps", async () => {
    const server = serverFor();
    const { root, nav } = await startTestShell("/changes/c1", server);
    await until(() => shownPath(root) === "proposal.md");

    const design = [...root.querySelectorAll<HTMLAnchorElement>(".doc-nav a")].find(
      (a) => a.textContent === "Design",
    );
    expect(design?.getAttribute("href")).toBe("/changes/c1?doc=design.md");
    design?.dispatchEvent(new MouseEvent("click", { bubbles: true, button: 0 }));
    await until(() => shownPath(root) === "design.md");

    expect(nav.path()).toBe("/changes/c1?doc=design.md");
    expect(q(root, '.doc-nav a[aria-current="page"]').textContent).toBe("Design");

    // A reload at that address opens the same document.
    const reloaded = await startTestShell(
      "/changes/c1?doc=specs%2Fcli-bridge%2Fspec.md",
      serverFor(),
    );
    await until(() => shownPath(reloaded.root) === "specs/cli-bridge/spec.md");
    expect(q(reloaded.root, '.doc-nav a[aria-current="page"]').textContent).toBe("cli-bridge");
  });

  // Review fix 10.1: picking a document changes only the list and the article.
  it("switching documents fetches only the document, keeping the header and the live stream", async () => {
    const server = serverFor();
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => shownPath(root) === "proposal.md");
    const source = FakeEventSource.latest();
    const header = q(root, ".change-head");

    q(root, '.doc-nav a[title="design.md"]').dispatchEvent(
      new MouseEvent("click", { bubbles: true, button: 0 }),
    );
    await until(() => shownPath(root) === "design.md");

    expect(server.to("GET", "/api/changes/c1")).toHaveLength(1);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(source.closed).toBe(false);
    expect(q(root, ".change-head")).toBe(header);
  });

  it("Change with only tasks: lists only the tasks, and opens them", async () => {
    const server = serverFor(changePage({ documents: [doc("tasks.md", "tasks", "Tasks")] }));
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => shownPath(root) === "tasks.md");

    expect(links(root)).toEqual(["Tasks"]);
  });

  it("an unknown ?doc= falls back to the default document and says so", async () => {
    const { root } = await startTestShell("/changes/c1?doc=nope.md", serverFor());
    await until(() => shownPath(root) === "proposal.md");

    expect(q(root, ".doc-notice").textContent).toContain("no nope.md");
  });

  it("Unknown change: says it wasn't found and links back to the dashboard", async () => {
    const server = signedIn().on("GET", "/api/changes/zz", {
      status: 404,
      body: { detail: "Not found." },
    });
    const { root, heading } = await startTestShell("/changes/zz", server);
    await until(() => heading() === "Change not found");

    const back = [...root.querySelectorAll<HTMLAnchorElement>(".view a")].find(
      (a) => a.textContent === "Back to the dashboard",
    );
    expect(back?.getAttribute("href")).toBe("/");
    expect(server.calls.some((call) => call.path.includes("/documents/"))).toBe(false);
  });

  it("Report while reading: a change-updated for this change refreshes the shown document, with no reload", async () => {
    let version = 1;
    const server = serverFor().on("GET", "/api/changes/c1/documents/tasks.md", () => ({
      status: 200,
      body: { path: "tasks.md", html: `<p>tasks v${version}</p>` },
    }));
    const { root } = await startTestShell("/changes/c1?doc=tasks.md", server);
    await until(() => root.querySelector("article.doc")?.textContent === "tasks v1");
    const source = FakeEventSource.latest();
    source.open();
    const before = server.to("GET", "/api/changes/c1").length;

    // Another change's report leaves this page alone.
    source.emit(CHANGE_UPDATED, JSON.stringify(card({ changeId: "other" })));
    expect(server.to("GET", "/api/changes/c1")).toHaveLength(before);

    version = 2;
    source.emit(
      CHANGE_UPDATED,
      JSON.stringify(card({ changeId: "c1", lastReportedAt: "2026-11-08T11:59:00Z" })),
    );
    await until(() => root.querySelector("article.doc")?.textContent === "tasks v2");

    expect(server.to("GET", "/api/changes/c1")).toHaveLength(before + 1);
    expect(source.url).toBe("/api/events");
  });

  it("Archived while reading: says it has been archived, keeps its documents, and stops following", async () => {
    let archived = false;
    const server = serverFor().on("GET", "/api/changes/c1", () => ({
      status: 200,
      body: archived
        ? changePage({ archivedAt: "2026-11-08T12:00:00Z", archiveFolder: "2026-11-08-team-auth" })
        : changePage(),
    }));
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => shownPath(root) === "proposal.md");
    const source = FakeEventSource.latest();
    source.open();

    archived = true;
    source.emit(CHANGE_ARCHIVED, JSON.stringify({ changeId: "c1" }));
    await until(() => q(root, ".change-archived-status").textContent !== "");

    expect(q(root, ".change-archived-status").textContent).toContain(
      "has been archived as 2026-11-08-team-auth",
    );
    expect(q(root, ".change-when").textContent).toBe("Archived 8 Nov 2026");
    expect(shownPath(root)).toBe("proposal.md");
    expect(links(root)).toHaveLength(TEAM_AUTH.length);
    expect(source.closed).toBe(true);
  });

  it("an archived change's page is frozen: no live stream, and it links back to the history", async () => {
    const server = serverFor(
      changePage({ archivedAt: "2026-11-07T10:00:00Z", archiveFolder: "2026-11-07-team-auth" }),
    );
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => shownPath(root) === "proposal.md");

    expect(
      FakeEventSource.instances.filter((s) => s.url === "/api/events" && !s.closed),
    ).toHaveLength(0);
    expect(q(root, ".back-link").getAttribute("href")).toBe("/history");
    expect(q(root, ".change-when").textContent).toBe("Archived 7 Nov 2026");
  });

  it("leaving the page closes its stream", async () => {
    const { root, shell } = await startTestShell("/changes/c1", serverFor());
    await until(() => shownPath(root) === "proposal.md");
    const source = FakeEventSource.latest();

    await shell.router?.navigate("/members");

    expect(source.closed).toBe(true);
  });

  it("Signed out: sends no document content, and returns to the same change page after sign-in", async () => {
    const server = signedOut();
    const { nav, local } = await startTestShell("/changes/c1?doc=design.md", server);

    expect(nav.path()).toBe("/sign-in");
    expect(savedReturn(local)).toBe("/changes/c1?doc=design.md");
    expect(server.calls.some((call) => call.path.startsWith("/api/changes"))).toBe(false);
  });
});

const section = (
  overrides: Partial<SectionMetric> & Pick<SectionMetric, "number" | "title">,
): SectionMetric => ({
  startedAt: "2026-11-03T10:00:00Z",
  finishedAt: "2026-11-03T13:30:00Z",
  durationSeconds: 3 * 3600 + 1800,
  inProgress: false,
  ...overrides,
});

const metrics = (overrides: Partial<ChangeMetrics> = {}): ChangeMetrics => ({
  firstReportedAt: "2026-11-03T09:00:00Z",
  archivedAt: null,
  durationSeconds: 2 * 86400,
  active: true,
  sections: [],
  ...overrides,
});

const panel = (root: HTMLElement) => root.querySelector<HTMLElement>(".change-metrics");
// The visible text only: the screen-reader copy is left out, as a sighted reader sees it.
const shown = (element: Element | null) => {
  const copy = element?.cloneNode(true) as HTMLElement | undefined;
  copy?.querySelectorAll(".visually-hidden").forEach((hidden) => hidden.remove());
  return copy?.textContent ?? "";
};

// Spec change-metrics: Change page metrics.
describe("change page metrics", () => {
  it("Metrics on an archived change: took 5d 6h, labelled in words, and each section's time", async () => {
    const server = serverFor(
      changePage({ archivedAt: "2026-11-08T15:00:00Z", archiveFolder: "2026-11-08-team-auth" }),
    ).on("GET", "/api/changes/c1/metrics", {
      status: 200,
      body: metrics({
        archivedAt: "2026-11-08T15:00:00Z",
        active: false,
        durationSeconds: 5 * 86400 + 6 * 3600,
        sections: [
          section({ number: 1, title: "Server", durationSeconds: 2 * 3600 }),
          section({ number: 2, title: "Web" }),
        ],
      }),
    });
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => root.querySelector(".metrics-total") !== null);

    const total = q(root, ".metrics-total");
    expect(shown(total)).toBe("Took 5d 6h");
    expect(q(total, ".visually-hidden").textContent).toBe("5 days 6 hours");
    expect(q(total, ".duration").title).toBe("5 days 6 hours");
    expect(q(total, '[aria-hidden="true"]').textContent).toBe("5d 6h");
    expect(q(root, ".metrics-note").textContent).toBe("Wall-clock time, first report to archive.");
    expect([...root.querySelectorAll(".metrics-section")].map(shown)).toEqual([
      "1Server2h",
      "2Web3h 30m",
    ]);
    // Under the header, before the documents.
    expect(panel(root)?.previousElementSibling?.className).toBe("change-head");
  });

  it("an active change runs for its time so far, marking a section in progress", async () => {
    const server = serverFor().on("GET", "/api/changes/c1/metrics", {
      status: 200,
      body: metrics({
        sections: [
          section({ number: 1, title: "Server" }),
          section({
            number: 2,
            title: "Web",
            finishedAt: null,
            inProgress: true,
            durationSeconds: 3600,
          }),
        ],
      }),
    });
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => root.querySelector(".metrics-total") !== null);

    expect(shown(q(root, ".metrics-total"))).toBe("Running for 2d");
    expect(q(root, ".metrics-note").textContent).toBe("Wall-clock time since the first report.");
    const rows = [...root.querySelectorAll(".metrics-section")];
    expect(rows.map((row) => row.classList.contains("in-progress"))).toEqual([false, true]);
    expect(shown(q(rows[1] as HTMLElement, ".metrics-section-time"))).toBe(
      "in progress, 1h so far",
    );
  });

  it("Live while open: a report finishing section 3 shows it finished, without a reload", async () => {
    let finished = false;
    const server = serverFor().on("GET", "/api/changes/c1/metrics", () => ({
      status: 200,
      body: metrics({
        sections: [
          finished
            ? section({ number: 3, title: "Web", durationSeconds: 5400 })
            : section({
                number: 3,
                title: "Web",
                finishedAt: null,
                inProgress: true,
                durationSeconds: 3600,
              }),
        ],
      }),
    }));
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => root.querySelector(".metrics-section") !== null);
    expect(q(root, ".metrics-section").classList.contains("in-progress")).toBe(true);
    const source = FakeEventSource.latest();
    source.open();

    finished = true;
    source.emit(CHANGE_UPDATED, JSON.stringify(card({ changeId: "c1" })));
    await until(() => !q(root, ".metrics-section").classList.contains("in-progress"));

    expect(shown(q(root, ".metrics-section-time"))).toBe("1h 30m");
    expect(server.to("GET", "/api/changes/c1/metrics")).toHaveLength(2);
  });

  it("a change with no measured section says so", async () => {
    const server = serverFor().on("GET", "/api/changes/c1/metrics", {
      status: 200,
      body: metrics(),
    });
    const { root } = await startTestShell("/changes/c1", server);
    await until(() => root.querySelector(".metrics-empty") !== null);

    expect(q(root, ".metrics-empty").textContent).toBe("No section has been started yet.");
    expect(root.querySelector(".metrics-sections")).toBeNull();
  });

  it("a failed metrics load says so in the panel and leaves the documents alone", async () => {
    const server = serverFor().on("GET", "/api/changes/c1/metrics", {
      status: 500,
      body: { detail: "Boom." },
    });
    const { root } = await startTestShell("/changes/c1", server);
    await until(
      () => root.querySelector(".metrics-error") !== null && shownPath(root) === "proposal.md",
    );

    expect(q(root, ".metrics-error").textContent).toBe("Couldn't load this change's metrics.");
  });
});

describe("change page helpers", () => {
  it("link to a change, encoding the document path", () => {
    expect(changeHref("c1")).toBe("/changes/c1");
    expect(changeHref("c1", "specs/a b/spec.md")).toBe("/changes/c1?doc=specs%2Fa%20b%2Fspec.md");
  });

  it("open at the proposal, else the tasks, else the first document", () => {
    expect(defaultDocument(TEAM_AUTH)?.path).toBe("proposal.md");
    expect(defaultDocument(TEAM_AUTH.slice(1))?.path).toBe("tasks.md");
    expect(defaultDocument([doc("x.md", "other", "x.md")])?.path).toBe("x.md");
    expect(defaultDocument([])).toBeUndefined();
  });
});
