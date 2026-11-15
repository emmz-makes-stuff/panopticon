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

// spec repos, Repo web addresses (review fixes, §6).
describe("repoWebUrl, Azure DevOps hosts as reported", () => {
  it("matches a mixed-case Azure host and lowercases it in the link", () => {
    expect(
      repoWebUrl({ host: "Dev.Azure.com", owner: "cpointrepos/SMART-NOVA", name: "NOVA-INFRA" }),
    ).toBe("https://dev.azure.com/cpointrepos/SMART-NOVA/_git/NOVA-INFRA");
  });

  it("moves the old SSH host's v3/<org> into <org>.visualstudio.com", () => {
    expect(
      repoWebUrl({
        host: "vs-ssh.visualstudio.com",
        owner: "v3/cpoint/SMART-NOVA",
        name: "NOVA-INFRA",
      }),
    ).toBe("https://cpoint.visualstudio.com/SMART-NOVA/_git/NOVA-INFRA");
  });

  it("leaves a vs-ssh owner without v3/ as it is", () => {
    expect(
      repoWebUrl({ host: "vs-ssh.visualstudio.com", owner: "SMART-NOVA", name: "NOVA-INFRA" }),
    ).toBe("https://vs-ssh.visualstudio.com/SMART-NOVA/_git/NOVA-INFRA");
  });
});

// spec repos, Repos that get no link: "A dot segment".
describe("repoWebUrl with a dot segment", () => {
  it.each([
    { owner: "emmz", name: ".." },
    { owner: "emmz", name: "." },
    { owner: "a/../../x", name: "repo" },
  ])("gives no link for $owner/$name", ({ owner, name }) => {
    expect(repoWebUrl({ host: "github.com", owner, name })).toBeNull();
  });

  it("still links a name that merely contains dots", () => {
    expect(repoWebUrl({ host: "github.com", owner: "emmz", name: "my.repo" })).toBe(
      "https://github.com/emmz/my.repo",
    );
  });
});

describe("repoWebUrl with a host that isn't a plain hostname", () => {
  it.each([
    "github.com@evil.example",
    "evil.example/github.com",
    "evil.example#github.com",
    "evil.example?github.com",
    "github.com:8443",
    "evil.example\\github.com",
    "",
    "-bad.example",
  ])("gives no link for %j", (host) => {
    expect(repoWebUrl({ host, owner: "emmz", name: "panopticon" })).toBeNull();
  });

  it("makes plain text, not a link", () => {
    const text = repoLink(
      { host: "github.com@evil.example", owner: "emmz", name: "panopticon" },
      "card-repo-name",
    );
    expect([text.tagName, text.className, text.textContent]).toEqual([
      "SPAN",
      "card-repo-name",
      "emmz/panopticon",
    ]);
  });
});

describe("repoLink", () => {
  const link = repoLink(
    { host: "github.com", owner: "emmz-makes-stuff", name: "panopticon" },
    "card-repo-name",
  ) as HTMLAnchorElement;

  it("shows owner/name and links to the repo's page in a new tab", () => {
    expect(link.textContent).toBe("emmz-makes-stuff/panopticon");
    expect(link.href).toBe("https://github.com/emmz-makes-stuff/panopticon");
    expect(link.target).toBe("_blank");
    expect(link.rel).toBe("noopener noreferrer");
    expect(link.title).toBe("Open emmz-makes-stuff/panopticon on github.com");
  });

  it("ends with a decorative external mark, and says it opens a new tab", () => {
    const mark = link.querySelector("svg.external-mark");
    expect(mark?.getAttribute("aria-hidden")).toBe("true");
    expect(link.getAttribute("aria-label")).toBe("emmz-makes-stuff/panopticon, opens in a new tab");
  });

  it("keeps the caller's class and isn't a route link", () => {
    expect([...link.classList]).toEqual(["card-repo-name", "repo-link"]);
    expect(link.dataset["route"]).toBeUndefined();
  });
});
