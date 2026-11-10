import { afterEach, describe, expect, it } from "vitest";
import { signedIn, startTestShell } from "./testShell";
import { PROJECT_TAGS_CHANGED } from "./live";
import { card, FakeEventSource, q, until, type FakeServer } from "./testing";
import type { ChangeCard, ChangePage } from "./types";
import type { ProjectDetails, ProjectRole, ProjectTag } from "./views/projects";

const PAN = { id: "p1", code: "PAN", name: "Panopticon" };
const tag = (id: string, key: string, value: string | null): ProjectTag => ({
  id,
  key,
  value,
  text: value === null ? key : `${key}: ${value}`,
  changeCount: 1,
});
const BUG = tag("t1", "bug", null);
const AREA = tag("t2", "area", "onboarding");

const panCard = (projectTags: ChangeCard["projectTags"] = [BUG]) =>
  card({
    changeId: "c1",
    change: "team-auth",
    project: PAN,
    projectTags,
    personalTags: [{ key: "dev", value: null }],
  });

const page = (c: ChangeCard): ChangePage => ({
  card: c,
  archivedAt: null,
  archiveFolder: null,
  documents: [],
});

function serverFor(
  role: ProjectRole,
  c: ChangeCard = panCard(),
  tags: ProjectTag[] = [AREA, BUG],
): FakeServer {
  const details: ProjectDetails = { ...PAN, role, members: [], tags };
  return signedIn()
    .on("GET", "/api/changes/c1", { status: 200, body: page(c) })
    .on("GET", "/api/projects/p1", { status: 200, body: details });
}

// In the document, so checkboxes toggle and focus moves as in a browser (a detached checkbox fires no change).
const attached: HTMLElement[] = [];
afterEach(() => {
  for (const node of attached.splice(0)) {
    node.remove();
  }
});

async function open(server: FakeServer) {
  const shell = await startTestShell("/changes/c1", server);
  document.body.append(shell.events);
  attached.push(shell.events);
  await until(() => shell.root.querySelector(".change-tags .tag-chips") !== null);
  return shell;
}

const toggleButton = (root: HTMLElement) =>
  root.querySelector<HTMLButtonElement>(".tag-picker-toggle");
const chipTexts = (root: HTMLElement) =>
  [...root.querySelectorAll(".change-tags .tag-chip")].map((chip) => chip.textContent);
const options = (root: HTMLElement) =>
  [...root.querySelectorAll<HTMLLabelElement>(".tag-picker-option")].map((o) => ({
    text: o.textContent,
    checked: o.querySelector("input")?.checked,
  }));

async function openPanel(root: HTMLElement) {
  await until(() => toggleButton(root) !== null);
  toggleButton(root)?.click();
  await until(() => options(root).length > 0);
}

function type(root: HTMLElement, text: string) {
  const search = q<HTMLInputElement>(root, ".tag-picker input[type=search]");
  search.value = text;
  search.dispatchEvent(new Event("input"));
}

function check(root: HTMLElement, text: string) {
  const option = [...root.querySelectorAll(".tag-picker-option")].find(
    (o) => o.textContent === text,
  );
  const box = option?.querySelector("input");
  if (!box) {
    throw new Error(`No option ${text}`);
  }

  box.click();
}

// Spec tags: "Tagging a change on the dashboard", "Adding a tag from the picker" (design D4, D9).
describe("change page tag picker", () => {
  it("A viewer can't: the chips show, with no way to edit them", async () => {
    const server = serverFor("viewer");
    const { root } = await open(server);
    await until(() => server.to("GET", "/api/projects/p1").length === 1);

    expect(chipTexts(root)).toEqual(["PAN/bug", "dev (only you see this)"]);
    expect(toggleButton(root)).toBeNull();
  });

  it("An editor tags a change: the panel lists the Project's tags, checked where the change has them", async () => {
    const server = serverFor("editor").on("POST", "/api/changes/c1/tags", (call) => {
      expect(call.body).toEqual({ add: ["t2"] });
      return { status: 200, body: panCard([AREA, BUG]) };
    });
    const { root } = await open(server);

    await openPanel(root);
    expect(toggleButton(root)?.textContent).toBe("Done");
    expect(toggleButton(root)?.getAttribute("aria-expanded")).toBe("true");
    expect(q(root, ".tag-picker legend").textContent).toBe("PAN's tags");
    expect(options(root)).toEqual([
      { text: "area: onboarding", checked: false },
      { text: "bug", checked: true },
    ]);

    check(root, "area: onboarding");
    await until(() => q(root, ".tag-picker-status").textContent !== "");

    expect(q(root, ".tag-picker-status").textContent).toBe("Added PAN/area: onboarding.");
    expect(chipTexts(root)).toEqual(["PAN/area: onboarding", "PAN/bug", "dev (only you see this)"]);
  });

  it("unchecking removes the tag; a refusal puts the box back and says why", async () => {
    let refuse = false;
    const server = serverFor("owner").on("POST", "/api/changes/c1/tags", (call) => {
      if (refuse) {
        return {
          status: 403,
          body: { detail: "Only the Project's owners and editors can tag its changes." },
        };
      }

      expect(call.body).toEqual({ remove: ["t1"] });
      return { status: 200, body: panCard([]) };
    });
    const { root } = await open(server);
    await openPanel(root);

    check(root, "bug");
    await until(() => q(root, ".tag-picker-status").textContent === "Removed PAN/bug.");
    expect(chipTexts(root)).toEqual(["dev (only you see this)"]);

    refuse = true;
    check(root, "area: onboarding");
    await until(() => !!root.querySelector(".tag-picker [role=alert]")?.textContent);
    expect(q(root, ".tag-picker [role=alert]").textContent).toBe(
      "Only the Project's owners and editors can tag its changes.",
    );
    expect(options(root).find((o) => o.text === "area: onboarding")?.checked).toBe(false);
  });

  it("A new tag from the picker: + Add makes it a Project tag and puts it on the change", async () => {
    const server = serverFor("editor")
      .on("POST", "/api/projects/p1/tags", (call) => {
        expect(call.body).toEqual({ tag: "area: billing" });
        return { status: 201, body: tag("t9", "area", "billing") };
      })
      .on("POST", "/api/changes/c1/tags", (call) => {
        expect(call.body).toEqual({ add: ["t9"] });
        return { status: 200, body: panCard([tag("t9", "area", "billing"), BUG]) };
      });
    const { root } = await open(server);
    await openPanel(root);

    type(root, "area: billing");
    const add = q<HTMLButtonElement>(root, ".tag-picker-add");
    expect(add.textContent).toBe("+ Add PAN/area: billing");
    add.click();
    await until(() => q(root, ".tag-picker-status").textContent !== "");

    expect(q(root, ".tag-picker-status").textContent).toBe(
      "Added PAN/area: billing to PAN's tags and to this change.",
    );
    expect(chipTexts(root)).toContain("PAN/area: billing");
    expect(options(root).find((o) => o.text === "area: billing")?.checked).toBe(true);
  });

  it("no + Add for text equal to a tag the Project has, ignoring case; a malformed tag says what's wrong", async () => {
    const { root } = await open(serverFor("editor"));
    await openPanel(root);

    type(root, "Area: Onboarding");
    expect(root.querySelector(".tag-picker-add")).toBeNull();
    expect(options(root).map((o) => o.text)).toEqual(["area: onboarding"]);

    type(root, "has space");
    expect(root.querySelector(".tag-picker-add")).toBeNull();
    expect(q(root, ".tag-picker [role=alert]").textContent).toBe(
      "\"has space\": a key can only have letters, digits, '-', '_' and '.'.",
    );
  });

  it("a Project with no tags invites typing one", async () => {
    const server = serverFor("editor", panCard([]), []);
    const shell = await startTestShell("/changes/c1", server);
    document.body.append(shell.events);
    attached.push(shell.events);
    await until(() => toggleButton(shell.root) !== null);

    toggleButton(shell.root)?.click();

    expect(q(shell.root, ".tag-picker-empty").textContent).toBe(
      "PAN has no tags yet. Type one to add it.",
    );
  });

  it("Escape closes the panel and gives focus back to the button", async () => {
    const { root } = await open(serverFor("editor"));
    await openPanel(root);

    q(root, ".tag-picker").dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
    );

    expect(q(root, ".tag-picker").hidden).toBe(true);
    expect(toggleButton(root)?.textContent).toBe("Edit tags");
    expect(document.activeElement).toBe(toggleButton(root));
  });

  it("a personal change has no project tags and no picker, and no Project is asked for", async () => {
    const personal = card({
      changeId: "c1",
      personal: true,
      personalTags: [{ key: "dev", value: null }],
    });
    const server = signedIn().on("GET", "/api/changes/c1", { status: 200, body: page(personal) });
    const { root } = await open(server);

    expect(chipTexts(root)).toEqual(["dev (only you see this)"]);
    expect(toggleButton(root)).toBeNull();
    expect(server.calls.some((c) => c.path.startsWith("/api/projects"))).toBe(false);
  });
});

// Spec tags, "Tag edits are live" (the change page; tags D6).
describe("change page tags, live", () => {
  it("A rename shows in the header and the open picker, with no refetch of the page", async () => {
    const server = serverFor("editor");
    const { root } = await open(server);
    await openPanel(root);
    const source = FakeEventSource.latest();
    source.open();
    const pageLoads = server.to("GET", "/api/changes/c1").length;
    const projectLoads = server.to("GET", "/api/projects/p1").length;

    // Another Project's tags leave the page alone.
    source.emit(PROJECT_TAGS_CHANGED, JSON.stringify({ projectId: "acme-id", tags: [] }));
    expect(server.to("GET", "/api/projects/p1")).toHaveLength(projectLoads);

    const DEFECT = tag("t1", "defect", null);
    const NEW = tag("t3", "new", null);
    server.on("GET", "/api/projects/p1", {
      status: 200,
      body: { ...PAN, role: "editor", members: [], tags: [AREA, DEFECT, NEW] },
    });
    source.emit(
      PROJECT_TAGS_CHANGED,
      JSON.stringify({
        projectId: "P1",
        tags: [AREA, DEFECT, NEW].map(({ id, key, value }) => ({ id, key, value })),
      }),
    );

    expect(chipTexts(root)).toEqual(["PAN/defect", "dev (only you see this)"]);
    await until(() => options(root).some((o) => o.text === "new"));
    expect(options(root)).toEqual([
      { text: "area: onboarding", checked: false },
      { text: "defect", checked: true },
      { text: "new", checked: false },
    ]);
    expect(toggleButton(root)?.getAttribute("aria-expanded")).toBe("true");
    expect(server.to("GET", "/api/changes/c1")).toHaveLength(pageLoads);
  });
});

describe("change page tags, live, after a toggle", () => {
  it("A rename keeps a tag just put on in the picker, before the page hears of it", async () => {
    const server = serverFor("editor").on("POST", "/api/changes/c1/tags", {
      status: 200,
      body: panCard([AREA, BUG].map(({ id, key, value }) => ({ id, key, value }))),
    });
    const { root } = await open(server);
    await openPanel(root);
    const source = FakeEventSource.latest();
    source.open();
    check(root, "area: onboarding");
    await until(() => chipTexts(root).length === 3);

    source.emit(
      PROJECT_TAGS_CHANGED,
      JSON.stringify({
        projectId: "p1",
        tags: [
          { id: "t2", key: "area", value: "onboarding" },
          { id: "t1", key: "defect", value: null },
        ],
      }),
    );

    expect(chipTexts(root)).toEqual([
      "PAN/area: onboarding",
      "PAN/defect",
      "dev (only you see this)",
    ]);
  });
});

describe("change page tags, choosing another document", () => {
  it("keeps a tag just put on, on an archived change with no live stream", async () => {
    const archived: ChangePage = {
      card: panCard(),
      archivedAt: "2026-11-09T10:00:00Z",
      archiveFolder: "2026-11-09-team-auth",
      documents: [
        { path: "proposal.md", kind: "proposal", title: "Proposal", sizeBytes: 10 },
        { path: "design.md", kind: "design", title: "Design", sizeBytes: 10 },
      ],
    };
    const html = (path: string) => ({ status: 200, body: { path, html: `<h1>${path}</h1>` } });
    const server = serverFor("editor")
      .on("GET", "/api/changes/c1", { status: 200, body: archived })
      .on("GET", "/api/changes/c1/documents/proposal.md", html("proposal.md"))
      .on("GET", "/api/changes/c1/documents/design.md", html("design.md"))
      .on("POST", "/api/changes/c1/tags", {
        status: 200,
        body: panCard([AREA, BUG].map(({ id, key, value }) => ({ id, key, value }))),
      });
    const { root } = await open(server);
    await openPanel(root);
    check(root, "area: onboarding");
    await until(() => chipTexts(root).length === 3);

    [...root.querySelectorAll<HTMLAnchorElement>(".doc-nav a")]
      .find((a) => a.textContent === "Design")
      ?.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, button: 0 }));
    await until(
      () => root.querySelector<HTMLElement>("article.doc")?.dataset["path"] === "design.md",
    );

    expect(chipTexts(root)).toEqual(["PAN/area: onboarding", "PAN/bug", "dev (only you see this)"]);
  });
});
