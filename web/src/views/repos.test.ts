import { describe, expect, it } from "vitest";
import { signedIn, signedOut, savedReturn, startTestShell } from "../testShell";
import { q, until } from "../testing";
import type { RepoSummary } from "../types";

// spec repos: Repos list, Archived changes on the Repos page, Repo links, Repos in the header; repos-page D5.
const PAN = { id: "p1", code: "PAN", name: "Panopticon" };

const panopticon: RepoSummary = {
  repo: { host: "github.com", owner: "emmz-makes-stuff", name: "panopticon" },
  latestActivityAt: "2026-11-08T10:00:00Z",
  active: [
    {
      changeId: "rp",
      change: "repos-page",
      project: PAN,
      personal: false,
      lastReportedAt: "2026-11-08T10:00:00Z",
    },
  ],
  archived: [
    {
      changeId: "vr",
      change: "visit-requests",
      project: null,
      personal: true,
      archivedAt: "2026-11-07T09:00:00Z",
    },
  ],
  moreArchived: 0,
};

const novaInfra: RepoSummary = {
  repo: { host: "dev.azure.com", owner: "cpointrepos/SMART-NOVA", name: "NOVA-INFRA" },
  latestActivityAt: "2026-11-06T09:00:00Z",
  active: [],
  archived: [1, 2, 3, 4, 5].map((day) => ({
    changeId: `n${day}`,
    change: `infra-${day}`,
    project: null,
    personal: false,
    archivedAt: `2026-11-0${day}T09:00:00Z`,
  })),
  moreArchived: 3,
};

const reposServer = (repos: RepoSummary[]) =>
  signedIn().on("GET", "/api/repos", { status: 200, body: { repos } });

const click = (link: Element | null | undefined) =>
  link?.dispatchEvent(new MouseEvent("click", { bubbles: true, button: 0 }));

const sections = (root: HTMLElement) =>
  [...root.querySelectorAll<HTMLElement>("section.repo")].map((section) => ({
    heading: section.querySelector(".repo-heading")?.textContent,
    changes: [...section.querySelectorAll<HTMLElement>(".repo-change")].map(
      (item) => item.textContent,
    ),
  }));

describe("repos page", () => {
  it("A repo with active and archived changes: active first, each linking to its change page", async () => {
    const { root, heading } = await startTestShell("/repos", reposServer([panopticon, novaInfra]));
    await until(() => sections(root).length === 2);

    expect(heading()).toBe("Repos");
    expect(sections(root)[0]).toEqual({
      heading: "github.com emmz-makes-stuff/panopticon",
      changes: ["repos-pagePAN Reported 2h ago", "visit-requestsPersonal Archived 7 Nov 2026"],
    });
    const links = [
      ...root.querySelectorAll<HTMLAnchorElement>("section.repo"),
    ][0]?.querySelectorAll<HTMLAnchorElement>(".repo-change-link");
    expect([...(links ?? [])].map((a) => [a.getAttribute("href"), a.dataset["route"]])).toEqual([
      ["/changes/rp", ""],
      ["/changes/vr", ""],
    ]);
  });

  it("each repo's name links to its web page in a new tab", async () => {
    const { root } = await startTestShell("/repos", reposServer([panopticon, novaInfra]));
    await until(() => sections(root).length === 2);

    expect(
      [...root.querySelectorAll<HTMLAnchorElement>(".repo-heading a.repo-name")].map((a) => [
        a.href,
        a.target,
      ]),
    ).toEqual([
      ["https://github.com/emmz-makes-stuff/panopticon", "_blank"],
      ["https://dev.azure.com/cpointrepos/SMART-NOVA/_git/NOVA-INFRA", "_blank"],
    ]);
  });

  it("More than five archived: the rest are a link to History filtered to the repo", async () => {
    const { root, nav } = await startTestShell("/repos", reposServer([novaInfra]));
    await until(() => sections(root).length === 1);

    const more = q<HTMLAnchorElement>(root, ".repo-more a");
    expect(more.textContent).toBe("3 more in History");
    expect(more.getAttribute("href")).toBe(
      "/history?repo=dev.azure.com%2Fcpointrepos%2FSMART-NOVA%2FNOVA-INFRA",
    );
    expect(root.querySelectorAll(".repo-change")).toHaveLength(5);
    click(more);
    await until(() => nav.path() === "/history");
  });

  it("Nothing reported yet: an empty state says where repos come from", async () => {
    const { root } = await startTestShell("/repos", reposServer([]));
    await until(() => root.querySelector(".repos-empty") !== null);

    expect(q(root, ".repos-empty").textContent).toBe(
      "No repos yet.A repo appears here once an agent reports progress on one of its changes.",
    );
  });

  it("a failed load says so and offers to try again", async () => {
    const server = signedIn().on("GET", "/api/repos", {
      status: 500,
      body: { detail: "Something broke." },
    });
    const { root } = await startTestShell("/repos", server);
    await until(() => !q(root, ".show-more").hidden);

    expect(q(root, ".form-error").textContent).not.toBe("");
    expect(q(root, ".show-more").textContent).toBe("Try again");
  });

  it("Signed out: returns to the Repos page after sign-in", async () => {
    const { nav, local } = await startTestShell("/repos", signedOut());

    expect(nav.path()).toBe("/sign-in");
    expect(savedReturn(local)).toBe("/repos");
  });

  it("Opening the Repos page: the header's Repos link shows it in place", async () => {
    const { root, nav, heading } = await startTestShell("/", reposServer([panopticon]));

    click([...root.querySelectorAll("nav.account a")].find((a) => a.textContent === "Repos"));
    await until(() => heading() === "Repos");

    await until(() => sections(root).length === 1);
    expect(nav.path()).toBe("/repos");
  });
});
