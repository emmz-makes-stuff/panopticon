import { describe, expect, it } from "vitest";
import { repoLink } from "./dom";
import { repoWebUrl } from "./links";

// spec repos: Repo web addresses, Repo links; repos-page D2, D3.
describe("repoWebUrl", () => {
  it("puts _git before the name on Azure DevOps", () => {
    expect(
      repoWebUrl({ host: "dev.azure.com", owner: "cpointrepos/SMART-NOVA", name: "NOVA-INFRA" }),
    ).toBe("https://dev.azure.com/cpointrepos/SMART-NOVA/_git/NOVA-INFRA");
  });

  it("puts _git before the name on an old visualstudio.com host", () => {
    expect(
      repoWebUrl({ host: "cpoint.visualstudio.com", owner: "SMART-NOVA", name: "NOVA-INFRA" }),
    ).toBe("https://cpoint.visualstudio.com/SMART-NOVA/_git/NOVA-INFRA");
  });

  it("keeps a nested GitLab group as the owner", () => {
    expect(repoWebUrl({ host: "gitlab.com", owner: "team/platform", name: "api" })).toBe(
      "https://gitlab.com/team/platform/api",
    );
  });

  it("is host, owner and name on GitHub", () => {
    expect(repoWebUrl({ host: "github.com", owner: "emmz-makes-stuff", name: "panopticon" })).toBe(
      "https://github.com/emmz-makes-stuff/panopticon",
    );
  });

  it("encodes each segment but keeps the owner's slashes", () => {
    expect(repoWebUrl({ host: "example.org", owner: "a team/b?c", name: "my repo" })).toBe(
      "https://example.org/a%20team/b%3Fc/my%20repo",
    );
  });

  it("doesn't treat a look-alike host as Azure DevOps", () => {
    expect(repoWebUrl({ host: "notvisualstudio.com", owner: "o", name: "n" })).toBe(
      "https://notvisualstudio.com/o/n",
    );
  });
});

describe("repoLink", () => {
  const link = repoLink(
    { host: "github.com", owner: "emmz-makes-stuff", name: "panopticon" },
    "card-repo-name",
  );

  it("shows owner/name and links to the repo's page in a new tab", () => {
    expect(link.textContent).toBe("emmz-makes-stuff/panopticon");
    expect(link.href).toBe("https://github.com/emmz-makes-stuff/panopticon");
    expect(link.target).toBe("_blank");
    expect(link.rel).toBe("noopener noreferrer");
    expect(link.title).toBe("Open emmz-makes-stuff/panopticon on github.com");
  });

  it("keeps the caller's class and isn't a route link", () => {
    expect([...link.classList]).toEqual(["card-repo-name", "repo-link"]);
    expect(link.dataset["route"]).toBeUndefined();
  });
});
