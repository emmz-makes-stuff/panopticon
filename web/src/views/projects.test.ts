import { beforeEach, describe, expect, it, vi } from "vitest";
import { createApi } from "../api";
import { PROJECT_TAGS_CHANGED } from "../live";
import { FakeEventSource, MEMBER, fakeServer, q, settle, until, type FakeServer } from "../testing";
import { signedIn, startTestShell, type TestShellOptions } from "../testShell";
import { codePlate } from "../projectLabel";
import { SETUP_GUIDE_HIDDEN_KEY } from "./setupGuide";
import {
  linkFile,
  projectTagSection,
  type Project,
  type ProjectDetails,
  type ProjectMember,
  type ProjectTag,
} from "./projects";

const PAN_ID = "6f1c2e4a-0000-4000-8000-000000000001";
const pan: Project = { id: PAN_ID, name: "Panopticon", code: "PAN", role: "owner" };
const acme: Project = { id: "a1", name: "Acme client work", code: "ACME", role: "viewer" };

const emmz: ProjectMember = {
  memberId: MEMBER.id,
  email: MEMBER.email,
  displayName: MEMBER.displayName,
  role: "owner",
  joinedAt: "2026-11-01T09:00:00Z",
};
const sam: ProjectMember = {
  memberId: "m2",
  email: "sam@example.com",
  displayName: "Sam",
  role: "viewer",
  joinedAt: "2026-11-08T10:00:00Z",
};

const details = (
  role: Project["role"],
  members: ProjectMember[] = [emmz, sam],
  tags: ProjectTag[] = [],
): ProjectDetails => ({
  ...pan,
  role,
  members: members.map((m) => (m.memberId === MEMBER.id ? { ...m, role } : m)),
  tags,
});

const projectTag = (
  id: string,
  key: string,
  value: string | null,
  changeCount: number,
): ProjectTag => ({
  id,
  key,
  value,
  text: value === null ? key : `${key}: ${value}`,
  changeCount,
});

const PATH = `/api/projects/${PAN_ID}`;

function submit(form: Element | null) {
  if (!form) {
    throw new Error("No form");
  }

  form.dispatchEvent(new Event("submit", { cancelable: true }));
}

const listRows = (root: HTMLElement) =>
  [...root.querySelectorAll(".project")].map((row) => ({
    code: row.querySelector(".project-code")?.textContent,
    name: row.querySelector(".project-name")?.textContent,
    href: row.querySelector(".project-name")?.getAttribute("href"),
    role: row.querySelector(".project-role")?.textContent,
  }));

const memberRows = (root: HTMLElement) =>
  [...root.querySelectorAll(".project-members .member")].map((row) => ({
    name: row.querySelector(".member-name")?.textContent,
    email: row.querySelector(".member-email")?.textContent,
    role:
      row.querySelector<HTMLSelectElement>(".role-field select")?.value ??
      row.querySelector(".project-role")?.textContent,
    remove: row.querySelector(".danger-button") !== null,
  }));

// Spec projects: "Projects list", "Creating a Project", "Project codes".
describe("/projects", () => {
  async function openList(server: FakeServer) {
    const shell = await startTestShell("/projects", server);
    await until(() => shell.root.querySelector(".project, .project-empty") !== null);
    return shell;
  }

  it("Only your Projects: each with its code, name and your role, linking to its page", async () => {
    const { root, heading } = await openList(
      signedIn().on("GET", "/api/projects", { status: 200, body: [acme, pan] }),
    );

    expect(heading()).toBe("Projects");
    expect(listRows(root)).toEqual([
      { code: "ACME", name: "Acme client work", href: "/projects/a1", role: "Viewer" },
      { code: "PAN", name: "Panopticon", href: `/projects/${PAN_ID}`, role: "Owner" },
    ]);
  });

  it("shows a Project name with markup as text", async () => {
    const { root } = await openList(
      signedIn().on("GET", "/api/projects", {
        status: 200,
        body: [{ ...pan, name: "<b>x</b>" }],
      }),
    );

    expect(q(root, ".project-name").textContent).toBe("<b>x</b>");
    expect(root.querySelector(".project-name b")).toBeNull();
  });

  it("with no Projects, says so and how to get one", async () => {
    const { root } = await openList(
      signedIn().on("GET", "/api/projects", { status: 200, body: [] }),
    );

    expect(q(root, ".project-empty").textContent).toContain("You're not in any Projects yet.");
  });

  it("Create a Project: sends the trimmed name and the code in capitals, then opens its page", async () => {
    const server = signedIn()
      .on("GET", "/api/projects", { status: 200, body: [] })
      .on("POST", "/api/projects", { status: 201, body: pan })
      .on("GET", PATH, { status: 200, body: details("owner", [emmz]) });
    const { root, nav, heading } = await openList(server);
    q<HTMLInputElement>(root, ".project-form input[name=name]").value = "  Panopticon ";
    q<HTMLInputElement>(root, ".project-form input[name=code]").value = " pan ";

    submit(root.querySelector(".project-form"));
    await until(() => heading() === "Panopticon");

    expect(server.to("POST", "/api/projects")[0]?.body).toEqual({
      name: "Panopticon",
      code: "PAN",
    });
    expect(nav.location.pathname).toBe(`/projects/${PAN_ID}`);
  });

  it("the code field says what a code may be", async () => {
    const { root } = await openList(
      signedIn().on("GET", "/api/projects", { status: 200, body: [] }),
    );

    const code = q<HTMLInputElement>(root, ".project-form input[name=code]");
    const help = root.querySelector(`#${code.getAttribute("aria-describedby") ?? "none"}`);
    expect(help?.textContent).toBe("2 to 10 letters or digits, starting with a letter.");
  });

  it.each([
    ["Panopticon", "P-1", "A code is 2 to 10 letters or digits, starting with a letter."],
    ["Panopticon", "1PAN", "A code is 2 to 10 letters or digits, starting with a letter."],
    ["Panopticon", "P", "A code is 2 to 10 letters or digits, starting with a letter."],
    ["   ", "PAN", "A name is 1 to 100 characters."],
  ])(
    "Invalid code and Blank name: %j / %j is refused before sending",
    async (name, code, message) => {
      const server = signedIn().on("GET", "/api/projects", { status: 200, body: [] });
      const { root } = await openList(server);
      q<HTMLInputElement>(root, ".project-form input[name=name]").value = name;
      q<HTMLInputElement>(root, ".project-form input[name=code]").value = code;

      submit(root.querySelector(".project-form"));

      expect(server.to("POST", "/api/projects")).toHaveLength(0);
      expect(root.querySelector(".project-form .form-error")?.textContent).toBe(message);
    },
  );

  it("Code taken: shows the server's message and stays on the list", async () => {
    const detail = "The code PAN is taken. Choose another.";
    const server = signedIn()
      .on("GET", "/api/projects", { status: 200, body: [] })
      .on("POST", "/api/projects", { status: 409, body: { detail } });
    const { root, nav } = await openList(server);
    q<HTMLInputElement>(root, ".project-form input[name=name]").value = "Another";
    q<HTMLInputElement>(root, ".project-form input[name=code]").value = "pan";

    submit(root.querySelector(".project-form"));
    await until(() => !!root.querySelector(".project-form .form-error")?.textContent);

    expect(root.querySelector(".project-form .form-error")?.textContent).toBe(detail);
    expect(nav.location.pathname).toBe("/projects");
  });
});

// Spec projects: "Project page", "Membership roles", "Inviting to a Project", "Changing roles and removing".
describe("/projects/{id}", () => {
  async function openPage(server: FakeServer, options: TestShellOptions = {}) {
    const shell = await startTestShell(`/projects/${PAN_ID}`, server, options);
    await until(() => shell.root.querySelectorAll(".project-members .member").length > 0);
    return shell;
  }

  const withDetails = (body: ProjectDetails) => signedIn().on("GET", PATH, { status: 200, body });

  it("Linking text: shows the code, name and id, and the .panopticon.json naming the id, ready to copy", async () => {
    const copy = vi.fn(() => Promise.resolve());
    const { root, heading } = await openPage(withDetails(details("viewer")), { copy });

    expect(heading()).toBe("Panopticon");
    expect(q(root, ".project-code-large").textContent).toBe("PAN");
    expect(q(root, ".project-id").textContent).toBe(PAN_ID);
    expect(q(root, ".project-meta").textContent).toContain("You're a viewer.");
    const file = q(root, ".link-file").textContent ?? "";
    expect(JSON.parse(file)).toEqual({ project: PAN_ID });
    expect(file).toBe(linkFile(PAN_ID));

    q<HTMLButtonElement>(root, ".copy-row button").click();
    await until(() => q(root, ".copy-status").textContent === "Copied.");

    expect(copy).toHaveBeenCalledWith(linkFile(PAN_ID));
  });

  it("when the browser won't copy, says to select the text", async () => {
    const { root } = await openPage(withDetails(details("viewer")), {
      copy: () => Promise.reject(new Error("denied")),
    });

    q<HTMLButtonElement>(root, ".copy-row button").click();
    await until(() => !!q(root, ".copy-status").textContent);

    expect(q(root, ".copy-status").textContent).toBe("Couldn't copy. Select the text and copy it.");
  });

  it("Roles listed, as a viewer: names, emails and roles as text, with no rename, invite, role change or remove", async () => {
    const { root } = await openPage(
      withDetails(
        details("viewer", [
          { ...emmz, memberId: "m3", displayName: "Kit", email: "kit@example.com" },
          emmz,
        ]),
      ),
    );

    expect(memberRows(root)).toEqual([
      { name: "Kit", email: "kit@example.com", role: "Owner", remove: false },
      { name: "Emmz (you)", email: "emmz@example.com", role: "Viewer", remove: false },
    ]);
    expect(root.querySelector(".rename-form")).toBeNull();
    expect(root.querySelector(".invite-form")).toBeNull();
    expect(root.querySelector(".role-field")).toBeNull();
    expect(root.querySelector(".leave-button")?.textContent).toBe("Leave this Project");
  });

  // Spec projects, "An editor manages tags": the Tags controls, but not the owner's ones for people or the name.
  it("as an editor: the tag controls, with no rename, invite, role change or remove", async () => {
    const { root } = await openPage(withDetails(details("editor")));

    expect(memberRows(root).map((row) => row.remove)).toEqual([false, false]);
    expect(root.querySelector(".rename-form")).toBeNull();
    expect(root.querySelector(".invite-form")).toBeNull();
    expect(root.querySelector(".role-field")).toBeNull();
    expect(root.querySelector(".tag-add-form")).not.toBeNull();
  });

  it("as an owner: a role choice for everyone, and Remove for everyone but you", async () => {
    const { root } = await openPage(withDetails(details("owner")));

    expect(memberRows(root)).toEqual([
      { name: "Emmz (you)", email: "emmz@example.com", role: "owner", remove: false },
      { name: "Sam", email: "sam@example.com", role: "viewer", remove: true },
    ]);
    expect(
      q(root, ".project-members .member:nth-child(2) .role-field .field-label").textContent,
    ).toBe("Role for Sam");
    expect(root.querySelector(".rename-form")).not.toBeNull();
    expect(root.querySelector(".invite-form")).not.toBeNull();
  });

  it("Rename a Project: an owner saves the trimmed name, and the heading shows it", async () => {
    const server = withDetails(details("owner")).on("PATCH", PATH, (call) => ({
      status: 200,
      body: { ...details("owner"), ...(call.body as object) },
    }));
    const { root, heading } = await openPage(server);
    q<HTMLInputElement>(root, ".rename-form input").value = " Panopticon server ";

    submit(root.querySelector(".rename-form"));
    await until(() => heading() === "Panopticon server");

    expect(server.to("PATCH", PATH)[0]?.body).toEqual({ name: "Panopticon server" });
    expect(q(root, ".project-code-large").textContent).toBe("PAN");
  });

  it("Blank name: a rename to spaces sends nothing", async () => {
    const server = withDetails(details("owner"));
    const { root, heading } = await openPage(server);
    q<HTMLInputElement>(root, ".rename-form input").value = "   ";

    submit(root.querySelector(".rename-form"));

    expect(server.to("PATCH", PATH)).toHaveLength(0);
    expect(root.querySelector(".rename-form .form-error")?.textContent).toBe(
      "A name is 1 to 100 characters.",
    );
    expect(heading()).toBe("Panopticon");
  });

  it("Invite someone new: posts the address and role (viewer unless chosen), and the list shows them", async () => {
    let members = [emmz];
    const server = signedIn()
      .on("GET", PATH, () => ({ status: 200, body: details("owner", members) }))
      .on("POST", `${PATH}/members`, (call) => {
        const added = { ...sam, role: (call.body as { role: ProjectMember["role"] }).role };
        members = [...members, added];
        return { status: 201, body: added };
      });
    const { root } = await openPage(server);
    expect(q<HTMLSelectElement>(root, ".invite-form select[name=role]").value).toBe("viewer");
    q<HTMLInputElement>(root, ".invite-form input[name=email]").value = " sam@example.com ";
    q<HTMLSelectElement>(root, ".invite-form select[name=role]").value = "editor";

    submit(root.querySelector(".invite-form"));
    await until(() => memberRows(root).length === 2);

    expect(server.to("POST", `${PATH}/members`)[0]?.body).toEqual({
      email: "sam@example.com",
      role: "editor",
    });
    expect(memberRows(root)[1]).toMatchObject({ email: "sam@example.com", role: "editor" });
    expect(root.querySelector(".form-status")?.textContent).toBe(
      "Invited sam@example.com as an editor. They can sign in with that address now.",
    );
  });

  it.each([
    [400, "That isn't a valid email address."],
    [409, "sam@example.com is already in this Project."],
  ])(
    "Invalid address and Already in the Project: shows the server's message (%i)",
    async (status, detail) => {
      const server = withDetails(details("owner")).on("POST", `${PATH}/members`, {
        status,
        body: { detail },
      });
      const { root } = await openPage(server);
      q<HTMLInputElement>(root, ".invite-form input[name=email]").value = "not-an-email";

      submit(root.querySelector(".invite-form"));
      await until(() => !!root.querySelector(".invite-form .form-error")?.textContent);

      expect(root.querySelector(".invite-form .form-error")?.textContent).toBe(detail);
    },
  );

  it("Make a second owner: a role change is sent at once and confirmed", async () => {
    let members = [emmz, sam];
    const server = signedIn()
      .on("GET", PATH, () => ({ status: 200, body: details("owner", members) }))
      .on("PATCH", `${PATH}/members/m2`, (call) => {
        members = [emmz, { ...sam, role: (call.body as { role: ProjectMember["role"] }).role }];
        // As the server does: the Project's page as it is now.
        return { status: 200, body: details("owner", members) };
      });
    const { root } = await openPage(server);
    const loads = server.to("GET", PATH).length;
    const select = q<HTMLSelectElement>(root, ".project-members .member:nth-child(2) select");
    select.value = "owner";

    select.dispatchEvent(new Event("change"));
    await until(
      () =>
        memberRows(root)[1]?.role === "owner" && !!root.querySelector(".form-status")?.textContent,
    );

    expect(server.to("PATCH", `${PATH}/members/m2`)[0]?.body).toEqual({ role: "owner" });
    expect(root.querySelector(".form-status")?.textContent).toBe("Sam is now an owner.");
    expect(server.to("GET", PATH)).toHaveLength(loads);
  });

  it("Last owner stays: demoting yourself shows the message and puts the role back", async () => {
    const detail = "A Project needs an owner. Make someone else an owner first.";
    const server = withDetails(details("owner")).on("PATCH", `${PATH}/members/${MEMBER.id}`, {
      status: 409,
      body: { detail },
    });
    const { root } = await openPage(server);
    const select = q<HTMLSelectElement>(root, ".project-members .member:nth-child(1) select");
    select.value = "editor";

    select.dispatchEvent(new Event("change"));
    await until(() => !!root.querySelector(".project-members-block .form-error")?.textContent);

    expect(root.querySelector(".project-members-block .form-error")?.textContent).toBe(detail);
    expect(select.value).toBe("owner");
  });

  it("demoting yourself asks first, and cancelling sends nothing and keeps your role", async () => {
    const server = withDetails(details("owner"));
    const confirm = vi.fn(() => false);
    const { root } = await openPage(server, { confirm });
    const select = q<HTMLSelectElement>(root, ".project-members .member:nth-child(1) select");
    select.value = "editor";

    select.dispatchEvent(new Event("change"));

    expect(confirm).toHaveBeenCalledWith(
      "Make yourself an editor of PAN? You'll lose owner controls, and only another owner can give them back.",
    );
    expect(server.to("PATCH", `${PATH}/members/${MEMBER.id}`)).toHaveLength(0);
    expect(select.value).toBe("owner");
  });

  it("changing someone else's role doesn't ask", async () => {
    const server = withDetails(details("owner")).on("PATCH", `${PATH}/members/m2`, {
      status: 200,
      body: details("owner", [emmz, { ...sam, role: "editor" }]),
    });
    const confirm = vi.fn(() => true);
    const { root } = await openPage(server, { confirm });
    const select = q<HTMLSelectElement>(root, ".project-members .member:nth-child(2) select");
    select.value = "editor";

    select.dispatchEvent(new Event("change"));
    await until(() => server.to("PATCH", `${PATH}/members/m2`).length === 1);

    expect(confirm).not.toHaveBeenCalled();
  });

  it("Removed from a Project: asks first, then removes them from the list", async () => {
    let members = [emmz, sam];
    const server = signedIn()
      .on("GET", PATH, () => ({ status: 200, body: details("owner", members) }))
      .on("DELETE", `${PATH}/members/m2`, () => {
        members = [emmz];
        return { status: 204 };
      });
    const confirm = vi.fn((message: string) => message.length > 0);
    const { root } = await openPage(server, { confirm });

    q<HTMLButtonElement>(root, ".project-members .danger-button").click();
    await until(() => memberRows(root).length === 1);

    expect(confirm.mock.calls[0]?.[0]).toContain("Remove Sam (sam@example.com) from PAN?");
    expect(server.to("DELETE", `${PATH}/members/m2`)).toHaveLength(1);
  });

  it("cancelling the confirmation removes nobody", async () => {
    const server = withDetails(details("owner"));
    const { root } = await openPage(server, { confirm: () => false });

    q<HTMLButtonElement>(root, ".project-members .danger-button").click();

    expect(server.to("DELETE", `${PATH}/members/m2`)).toHaveLength(0);
  });

  it("Leave: a viewer leaves after confirming, and goes back to the list", async () => {
    const server = withDetails(details("viewer"))
      .on("DELETE", `${PATH}/members/${MEMBER.id}`, { status: 204 })
      .on("GET", "/api/projects", { status: 200, body: [] });
    const { root, nav, heading } = await openPage(server);

    q<HTMLButtonElement>(root, ".leave-button").click();
    await until(() => heading() === "Projects");

    expect(server.to("DELETE", `${PATH}/members/${MEMBER.id}`)).toHaveLength(1);
    expect(nav.location.pathname).toBe("/projects");
  });

  it("Last owner stays: leaving shows the message and stays on the page", async () => {
    const detail = "A Project needs an owner. Make someone else an owner first.";
    const server = withDetails(details("owner")).on("DELETE", `${PATH}/members/${MEMBER.id}`, {
      status: 409,
      body: { detail },
    });
    const { root, nav } = await openPage(server);

    q<HTMLButtonElement>(root, ".leave-button").click();
    await until(() => !!root.querySelector(".project-leave .form-error")?.textContent);

    expect(root.querySelector(".project-leave .form-error")?.textContent).toBe(detail);
    expect(nav.location.pathname).toBe(`/projects/${PAN_ID}`);
  });

  it("Only your Projects: a Project you aren't in says it wasn't found", async () => {
    const server = signedIn().on("GET", PATH, {
      status: 404,
      body: { detail: "There's no such Project." },
    });
    const { heading } = await startTestShell(`/projects/${PAN_ID}`, server);
    await until(() => heading() === "There's no such Project");

    expect(heading()).toBe("There's no such Project");
  });

  it("shows member names with markup as text", async () => {
    const { root } = await openPage(
      withDetails(details("owner", [emmz, { ...sam, displayName: "<b>Sam</b>" }])),
    );

    expect(q(root, ".project-members .member:nth-child(2) .member-name").textContent).toBe(
      "<b>Sam</b>",
    );
    expect(root.querySelector(".project-members b")).toBeNull();
  });
});

// Spec tags: "A Project's tags", "Managing a Project's tags", "Renaming a tag", "Deleting a tag" (the Project page;
// web/DESIGN.md "Tags").
describe("/projects/{id} tags", () => {
  const AREA = projectTag("t1", "area", "onboarding", 3);
  const BUG = projectTag("t2", "bug", null, 12);

  async function openTags(server: FakeServer, options: TestShellOptions = {}) {
    const shell = await startTestShell(`/projects/${PAN_ID}`, server, options);
    await until(() => shell.root.querySelector(".project-tags") !== null);
    return shell;
  }

  const tagRows = (root: HTMLElement) =>
    [...root.querySelectorAll(".project-tag")].map((row) => ({
      chip: row.querySelector(".tag-chip")?.textContent,
      count: row.querySelector(".project-tag-count")?.textContent,
      buttons: [...row.querySelectorAll("button")].map((b) => b.textContent),
    }));

  /** Clicks the button named `label` in the `index`th tag row; fails the test if there's none. */
  function clickTagButton(root: HTMLElement, index: number, label: string): void {
    const row = root.querySelectorAll<HTMLElement>(".project-tag")[index];
    const found = [...(row?.querySelectorAll<HTMLButtonElement>("button") ?? [])].find(
      (b) => b.textContent === label,
    );
    if (!found) {
      throw new Error(`No ${label} button in tag row ${index}`);
    }

    found.click();
  }

  /** A server whose Project page reflects `state.tags`, which a test changes as the server would. */
  function liveServer(role: Project["role"], state: { tags: ProjectTag[] }) {
    return signedIn().on("GET", PATH, () => ({
      status: 200,
      body: details(role, [emmz, sam], state.tags),
    }));
  }

  it("A viewer sees each tag with the code and its count, and no controls", async () => {
    const { root } = await openTags(
      liveServer("viewer", { tags: [AREA, BUG, projectTag("t3", "x", null, 1)] }),
    );

    expect(tagRows(root)).toEqual([
      { chip: "PAN/area: onboarding", count: "3 changes", buttons: [] },
      { chip: "PAN/bug", count: "12 changes", buttons: [] },
      { chip: "PAN/x", count: "1 change", buttons: [] },
    ]);
    expect(q(root, ".project-tags-hint").textContent).toBe("Owners and editors manage these tags.");
    expect(root.querySelector(".tag-add-form")).toBeNull();
  });

  it("a viewer of a Project with no tags is told so, with no form", async () => {
    const { root } = await openTags(liveServer("viewer", { tags: [] }));

    expect(q(root, ".project-tags-empty").textContent).toBe("PAN has no tags yet.");
    expect(root.querySelector(".tag-add-form")).toBeNull();
  });

  it("An editor adds a tag: it's sent, confirmed, and the list reloads", async () => {
    const state = { tags: [BUG] };
    const server = liveServer("editor", state).on("POST", `${PATH}/tags`, (call) => {
      state.tags = [projectTag("t9", "area", "billing", 0), BUG];
      expect(call.body).toEqual({ tag: "area: billing" });
      return { status: 201, body: state.tags[0] };
    });
    const { root } = await openTags(server);
    expect(tagRows(root)[0]?.buttons).toEqual(["Rename", "Delete"]);

    q<HTMLInputElement>(root, ".tag-add-form input").value = "  area: billing ";
    submit(root.querySelector(".tag-add-form"));
    await until(() => tagRows(root).length === 2);

    expect(q(root, ".form-status").textContent).toBe("Added PAN/area: billing.");
    expect(tagRows(root)[0]).toEqual({
      chip: "PAN/area: billing",
      count: "No changes",
      buttons: ["Rename", "Delete"],
    });
  });

  it("an editor of a Project with no tags is invited to add one, here or from a change's page", async () => {
    const { root } = await openTags(liveServer("editor", { tags: [] }));

    expect(q(root, ".project-tags-empty").textContent).toBe(
      "PAN has no tags yet. Add one here, or from a change's page.",
    );
    expect(root.querySelector(".tag-add-form")).not.toBeNull();
  });

  it("A duplicate: the server's refusal shows by the form, and the typed text stays", async () => {
    const server = liveServer("owner", { tags: [AREA] }).on("POST", `${PATH}/tags`, {
      status: 409,
      body: { detail: "PAN/area: onboarding is already one of this Project's tags." },
    });
    const { root } = await openTags(server);

    q<HTMLInputElement>(root, ".tag-add-form input").value = "Area: Onboarding";
    submit(root.querySelector(".tag-add-form"));
    await until(() => !!root.querySelector(".tag-add-form [role=alert]")?.textContent);

    expect(q(root, ".tag-add-form [role=alert]").textContent).toBe(
      "PAN/area: onboarding is already one of this Project's tags.",
    );
    expect(q<HTMLInputElement>(root, ".tag-add-form input").value).toBe("Area: Onboarding");
  });

  it("at 200 tags, the form's help says it's the most a Project can have", async () => {
    const tags = Array.from({ length: 200 }, (_, i) => projectTag(`t${i}`, `tag-${i}`, null, 0));
    const { root } = await openTags(liveServer("owner", { tags }));

    expect(q(root, ".tag-add-form .field-help").textContent).toBe(
      "PAN has 200 tags, the most it can have.",
    );
  });

  it("Renamed: rename in place, sent by id, confirmed, and the row shows the new name", async () => {
    const state = { tags: [AREA, BUG] };
    const server = liveServer("editor", state).on("PATCH", `${PATH}/tags/t2`, (call) => {
      expect(call.body).toEqual({ tag: "defect" });
      state.tags = [AREA, projectTag("t2", "defect", null, 12)];
      return { status: 200, body: state.tags[1] };
    });
    const { root } = await openTags(server);

    clickTagButton(root, 1, "Rename");
    const input = q<HTMLInputElement>(root, ".tag-rename-form input");
    expect(input.value).toBe("bug");
    expect(q(root, ".tag-rename-form .field-label").textContent).toBe("Rename PAN/bug");
    input.value = "defect";
    submit(root.querySelector(".tag-rename-form"));
    await until(() => tagRows(root)[1]?.chip === "PAN/defect");

    expect(q(root, ".form-status").textContent).toBe("Renamed PAN/bug to PAN/defect.");
  });

  it("a refused rename shows by its input; Escape gives the row back unchanged", async () => {
    const server = liveServer("editor", {
      tags: [AREA, projectTag("t3", "area", "billing", 1)],
    }).on("PATCH", `${PATH}/tags/t3`, {
      status: 409,
      body: { detail: "PAN/area: onboarding is already one of this Project's tags." },
    });
    const { root } = await openTags(server);

    clickTagButton(root, 1, "Rename");
    q<HTMLInputElement>(root, ".tag-rename-form input").value = "Area: Onboarding";
    submit(root.querySelector(".tag-rename-form"));
    await until(() => !!root.querySelector(".tag-rename-form [role=alert]")?.textContent);
    expect(q(root, ".tag-rename-form [role=alert]").textContent).toContain("PAN/area: onboarding");

    q(root, ".tag-rename-form").dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
    );

    expect(root.querySelector(".tag-rename-form")).toBeNull();
    expect(tagRows(root)[1]).toEqual({
      chip: "PAN/area: billing",
      count: "1 change",
      buttons: ["Rename", "Delete"],
    });
  });

  it("Confirm with a count: cancelling deletes nothing; confirming deletes and says so", async () => {
    const state = { tags: [AREA, BUG] };
    const server = liveServer("editor", state).on("DELETE", `${PATH}/tags/t2`, () => {
      state.tags = [AREA];
      return { status: 204 };
    });
    let answer = false;
    const confirm = vi.fn(() => answer);
    const { root } = await openTags(server, { confirm });
    const deleteBug = () => clickTagButton(root, 1, "Delete");

    deleteBug();
    expect(confirm).toHaveBeenCalledWith("PAN/bug is on 12 changes. Delete it?");
    expect(server.to("DELETE", `${PATH}/tags/t2`)).toHaveLength(0);

    answer = true;
    deleteBug();
    await until(() => tagRows(root).length === 1);

    expect(q(root, ".form-status").textContent).toBe("Deleted PAN/bug.");
  });

  it("deleting a tag no change carries says so in the confirm", async () => {
    const confirm = vi.fn(() => false);
    const { root } = await openTags(
      liveServer("owner", { tags: [projectTag("t4", "unused", null, 0)] }),
      { confirm },
    );

    clickTagButton(root, 0, "Delete");

    expect(confirm).toHaveBeenCalledWith("Delete PAN/unused? No changes carry it.");
  });
});

// project-page-live-tags D2: the Tags section redraws its rows in place, leaving the rest of the section alone.
describe("projectTagSection redraw", () => {
  const AREA = projectTag("t1", "area", "onboarding", 3);
  const BUG = projectTag("t2", "bug", null, 12);

  function open(role: Project["role"], tags: ProjectTag[]) {
    const status = document.createElement("p");
    const section = projectTagSection(
      {
        api: createApi({ fetch: fakeServer() }),
        path: PATH,
        confirm: () => false,
        status,
        reload: () => Promise.resolve(),
      },
      details(role, [emmz], tags),
    );
    document.body.replaceChildren(section.element, status);
    const redraw = (next: ProjectTag[]) => section.redraw(details(role, [emmz], next));
    return { root: section.element, status, redraw };
  }

  const rows = (root: HTMLElement) =>
    [...root.querySelectorAll(".project-tag")].map((row) =>
      row.querySelector(".tag-rename-form")
        ? `renaming ${row.querySelector<HTMLInputElement>("input")?.value}`
        : `${row.querySelector(".tag-chip")?.textContent} (${row.querySelector(".project-tag-count")?.textContent})`,
    );

  function startRename(root: HTMLElement, index: number): HTMLInputElement {
    const row = root.querySelectorAll<HTMLElement>(".project-tag")[index];
    const rename = [...(row?.querySelectorAll("button") ?? [])].find(
      (b) => b.textContent === "Rename",
    );
    if (!rename) {
      throw new Error(`No Rename button in tag row ${index}`);
    }

    rename.click();
    return q<HTMLInputElement>(row ?? root, ".tag-rename-form input");
  }

  it("A rename by someone else: the row shows the new name in its sorted place, with its count", () => {
    const { root, redraw } = open("viewer", [AREA, BUG]);

    redraw([BUG, projectTag("t1", "zone", null, 3)]);

    expect(rows(root)).toEqual(["PAN/bug (12 changes)", "PAN/zone (3 changes)"]);
  });

  it("An add from a change's page: the new tag joins with its count", () => {
    const { root, redraw } = open("viewer", [BUG]);

    redraw([projectTag("t9", "area", "billing", 1), BUG]);

    expect(rows(root)).toEqual(["PAN/area: billing (1 change)", "PAN/bug (12 changes)"]);
  });

  it("A delete: the tag leaves the list, and the last one gone shows the empty state", () => {
    const { root, redraw } = open("viewer", [AREA, BUG]);

    redraw([AREA]);
    expect(rows(root)).toEqual(["PAN/area: onboarding (3 changes)"]);

    redraw([]);
    expect(rows(root)).toEqual([]);
    expect(q(root, ".project-tags-empty").textContent).toBe("PAN has no tags yet.");
    expect(root.querySelector(".project-tags-hint")).toBeNull();
  });

  it("the first tag added to an empty Project replaces the empty state", () => {
    const { root, redraw } = open("viewer", []);

    redraw([BUG]);

    expect(root.querySelector(".project-tags-empty")).toBeNull();
    expect(rows(root)).toEqual(["PAN/bug (12 changes)"]);
    expect(q(root, ".project-tags-hint").textContent).toBe("Owners and editors manage these tags.");
  });

  it("Another tag changes: an open rename keeps its form, its typed text and focus", () => {
    const { root, redraw } = open("editor", [AREA, BUG]);
    const input = startRename(root, 1);
    input.value = "defect";

    redraw([projectTag("t1", "area", "billing", 3), BUG]);

    expect(rows(root)).toEqual(["PAN/area: billing (3 changes)", "renaming defect"]);
    expect(document.activeElement).toBe(input);
  });

  it("a row being renamed keeps focus even when its place in the order moves", () => {
    const { root, redraw } = open("editor", [AREA, BUG]);
    const input = startRename(root, 1);
    input.value = "defect";

    // area: onboarding became zone, so bug (being renamed) now sorts first and its row moves up.
    redraw([BUG, projectTag("t1", "zone", null, 3)]);

    expect(rows(root)).toEqual(["renaming defect", "PAN/zone (3 changes)"]);
    expect(document.activeElement).toBe(input);
  });

  it("Cancel on a kept rename shows the tag's name as it is now", () => {
    const { root, redraw } = open("editor", [BUG]);
    startRename(root, 0);

    redraw([projectTag("t2", "defect", null, 12)]);
    q(root, ".tag-rename-form").dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true }),
    );

    expect(rows(root)).toEqual(["PAN/defect (12 changes)"]);
  });

  it("The tag being renamed is deleted: the form closes, the row goes, and the status says so", () => {
    const { root, status, redraw } = open("editor", [AREA, BUG]);
    startRename(root, 1);

    redraw([AREA]);

    expect(root.querySelector(".tag-rename-form")).toBeNull();
    expect(rows(root)).toEqual(["PAN/area: onboarding (3 changes)"]);
    expect(status.textContent).toBe("PAN/bug was deleted.");
  });

  it("a tag deleted while nobody renames it says nothing", () => {
    const { status, redraw } = open("editor", [AREA, BUG]);

    redraw([AREA]);

    expect(status.textContent).toBe("");
  });

  it("The rest of the section stays put: text typed into the add form survives, and keeps focus", () => {
    const { root, redraw } = open("owner", [BUG]);
    const add = q<HTMLInputElement>(root, ".tag-add-form input");
    add.value = "area: bil";
    add.focus();

    redraw([projectTag("t9", "area", "billing", 1), BUG]);

    expect(q(root, ".tag-add-form input")).toBe(add);
    expect(add.value).toBe("area: bil");
    expect(document.activeElement).toBe(add);
  });

  it("the add form's help follows the count, at and below the limit", () => {
    const { root, redraw } = open("owner", [BUG]);
    const help = () => q(root, ".tag-add-form .field-help").textContent;
    const full = Array.from({ length: 200 }, (_, i) => projectTag(`t${i}`, `tag-${i}`, null, 0));

    redraw(full);
    expect(help()).toBe("PAN has 200 tags, the most it can have.");

    redraw([BUG]);
    expect(help()).toBe("A key, or a key and a value: bug, or area: onboarding.");
  });
});

// Spec tags, "The Project page's tags are live" and "The Project page catches up after a reconnect"
// (project-page-live-tags D1, D3, D4).
describe("/projects/{id} live tags", () => {
  const AREA = projectTag("t1", "area", "onboarding", 3);
  const BUG = projectTag("t2", "bug", null, 12);
  const tagsEvent = (tags: ProjectTag[], projectId = PAN_ID) =>
    JSON.stringify({
      projectId,
      tags: tags.map(({ id, key, value }) => ({ id, key, value })),
    });

  beforeEach(() => {
    FakeEventSource.instances.length = 0;
  });

  const openStreams = () =>
    FakeEventSource.instances.filter((s) => s.url === "/api/events" && !s.closed);

  const rows = (root: HTMLElement) =>
    [...root.querySelectorAll(".project-tag")].map(
      (row) =>
        `${row.querySelector(".tag-chip")?.textContent} (${row.querySelector(".project-tag-count")?.textContent})`,
    );

  /**
   * A server whose Project page reflects `state`, as the server would after another member's edit. `hold()` makes
   * the next page fetch answer only on `release()`, with the state it had when asked.
   */
  function liveServer(role: Project["role"], state: { tags: ProjectTag[]; gone?: boolean }) {
    const server = signedIn().on("GET", PATH, () =>
      state.gone
        ? { status: 404, body: { detail: "Not found." } }
        : { status: 200, body: details(role, [emmz, sam], state.tags) },
    );
    let holdNext = false;
    const held: (() => void)[] = [];
    const fetch = Object.assign(
      (input: string, init?: RequestInit) => {
        const answer = server(input, init);
        if (holdNext && (init?.method ?? "GET") === "GET" && input === PATH) {
          holdNext = false;
          return new Promise<Response>((resolve) => held.push(() => resolve(answer)));
        }

        return answer;
      },
      { calls: server.calls, on: server.on, to: server.to },
    ) as FakeServer;
    return {
      fetch,
      pageFetches: () => server.to("GET", PATH).length,
      hold: () => {
        holdNext = true;
      },
      release: () => held.shift()?.(),
    };
  }

  async function open(server: ReturnType<typeof liveServer>) {
    const shell = await startTestShell(`/projects/${PAN_ID}`, server.fetch);
    await until(() => shell.root.querySelector(".project-tags") !== null);
    const source = FakeEventSource.latest();
    source.open();
    return { ...shell, source };
  }

  it("A rename by someone else shows in its sorted place, with its count, without a reload", async () => {
    const state = { tags: [AREA, BUG] };
    const server = liveServer("viewer", state);
    const { root, source } = await open(server);

    state.tags = [BUG, projectTag("t1", "zone", null, 3)];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags));
    await until(() => rows(root)[0] === "PAN/bug (12 changes)");

    expect(rows(root)).toEqual(["PAN/bug (12 changes)", "PAN/zone (3 changes)"]);
  });

  it("An add from a change's page shows with its count, for a viewer", async () => {
    const state = { tags: [BUG] };
    const server = liveServer("viewer", state);
    const { root, source } = await open(server);

    state.tags = [projectTag("t9", "area", "billing", 1), BUG];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags));
    await until(() => rows(root).length === 2);

    expect(rows(root)[0]).toBe("PAN/area: billing (1 change)");
  });

  it("A delete: the tag leaves the list", async () => {
    const state = { tags: [AREA, BUG] };
    const server = liveServer("editor", state);
    const { root, source } = await open(server);

    state.tags = [AREA];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags));
    await until(() => rows(root).length === 1);

    expect(rows(root)).toEqual(["PAN/area: onboarding (3 changes)"]);
  });

  it("Another Project's tags: nothing is fetched, and the page doesn't change", async () => {
    const server = liveServer("viewer", { tags: [BUG] });
    const { root, source } = await open(server);
    const before = server.pageFetches();

    source.emit(PROJECT_TAGS_CHANGED, tagsEvent([], "acme-id"));
    await settle();

    expect(server.pageFetches()).toBe(before);
    expect(rows(root)).toEqual(["PAN/bug (12 changes)"]);
  });

  it("the Project's id matches without regard to case", async () => {
    const state = { tags: [BUG] };
    const server = liveServer("viewer", state);
    const { root, source } = await open(server);

    state.tags = [];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags, PAN_ID.toUpperCase()));
    await until(() => rows(root).length === 0);

    expect(rows(root)).toEqual([]);
  });

  it("a malformed event is ignored", async () => {
    const server = liveServer("viewer", { tags: [BUG] });
    const { source } = await open(server);
    const before = server.pageFetches();

    source.emit(PROJECT_TAGS_CHANGED, "not json");
    source.emit(PROJECT_TAGS_CHANGED, JSON.stringify({ projectId: PAN_ID }));
    await settle();

    expect(server.pageFetches()).toBe(before);
  });

  it("The rest of the page stays put: typed invite and add-tag text survive a live update", async () => {
    const state = { tags: [BUG] };
    const server = liveServer("owner", state);
    const { root, source } = await open(server);
    const email = q<HTMLInputElement>(root, ".invite-form input[type=email]");
    const add = q<HTMLInputElement>(root, ".tag-add-form input");
    email.value = "lee@exa";
    add.value = "area: bil";

    state.tags = [projectTag("t9", "area", "billing", 1), BUG];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags));
    await until(() => rows(root).length === 2);

    expect(q(root, ".invite-form input[type=email]")).toBe(email);
    expect(email.value).toBe("lee@exa");
    expect(q(root, ".tag-add-form input")).toBe(add);
    expect(add.value).toBe("area: bil");
  });

  it("an older answer arriving after a newer one doesn't overwrite it", async () => {
    const state = { tags: [BUG] };
    const server = liveServer("viewer", state);
    const { root, source } = await open(server);

    server.hold();
    state.tags = [projectTag("t1", "area", "one", 0), BUG];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags));
    state.tags = [projectTag("t1", "area", "two", 0), BUG];
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent(state.tags));
    await until(() => rows(root)[0] === "PAN/area: two (No changes)");

    server.release();
    await settle();

    expect(rows(root)[0]).toBe("PAN/area: two (No changes)");
  });

  it("a refresh that fails keeps the rows as they are", async () => {
    const server = liveServer("viewer", { tags: [BUG] });
    const { root, source } = await open(server);
    server.fetch.on("GET", PATH, { status: 500, body: { detail: "Boom." } });
    const before = server.pageFetches();

    source.emit(PROJECT_TAGS_CHANGED, tagsEvent([]));
    await settle();

    expect(server.pageFetches()).toBe(before + 1);
    expect(rows(root)).toEqual(["PAN/bug (12 changes)"]);
    expect(source.closed).toBe(false);
  });

  it("removed from the Project meanwhile: a refresh's 404 says it's not found, and the stream closes", async () => {
    const state: { tags: ProjectTag[]; gone?: boolean } = { tags: [BUG] };
    const server = liveServer("viewer", state);
    const { heading, source } = await open(server);

    state.gone = true;
    source.emit(PROJECT_TAGS_CHANGED, tagsEvent([]));
    await until(() => heading() === "There's no such Project");

    expect(source.closed).toBe(true);
  });

  it("a first load that 404s opens no stream", async () => {
    const server = liveServer("viewer", { tags: [], gone: true });
    const { heading } = await startTestShell(`/projects/${PAN_ID}`, server.fetch);
    await until(() => heading() === "There's no such Project");

    expect(openStreams()).toHaveLength(0);
  });

  it("the first open fetches nothing more", async () => {
    const server = liveServer("viewer", { tags: [BUG] });
    await open(server);
    await settle();

    expect(server.pageFetches()).toBe(1);
  });

  it("Missed while offline: a reconnect refetches once and shows a tag added meanwhile", async () => {
    const state = { tags: [BUG] };
    const server = liveServer("viewer", state);
    const { root, source } = await open(server);

    source.fail(0);
    state.tags = [projectTag("t9", "area", "billing", 0), BUG];
    source.open();
    await until(() => rows(root).length === 2);

    expect(rows(root)[0]).toBe("PAN/area: billing (No changes)");
    expect(server.pageFetches()).toBe(2);
  });

  it("a stream error changes nothing on the page, and opens no new stream", async () => {
    const server = liveServer("viewer", { tags: [BUG] });
    const { root, source } = await open(server);

    source.fail(2);
    await settle();

    expect(rows(root)).toEqual(["PAN/bug (12 changes)"]);
    expect(root.querySelector("[role=alert]")?.textContent ?? "").toBe("");
    expect(FakeEventSource.instances.filter((s) => s.url === "/api/events")).toHaveLength(1);
    expect(server.pageFetches()).toBe(1);
  });

  it("leaving the page closes its stream", async () => {
    const server = liveServer("viewer", { tags: [BUG] });
    const { shell, source } = await open(server);

    await shell.router?.navigate("/projects");

    expect(source.closed).toBe(true);
  });
});

describe("codePlate", () => {
  it("shows a code, or a Personal plate for work in no Project", () => {
    expect(codePlate("PAN").textContent).toBe("PAN");
    expect(codePlate("PAN").classList.contains("personal")).toBe(false);
    expect(codePlate(null).textContent).toBe("Personal");
    expect(codePlate(null).className).toBe("project-code personal");
  });
});

// Spec machine-setup, "Guide on the Projects page" (onboarding-guide D4).
describe("/projects setup guide", () => {
  const unused = {
    id: "t1",
    machineName: "vm-1",
    createdAt: "2026-11-01T09:00:00Z",
    lastUsedAt: null as string | null,
  };
  const reported = { ...unused, lastUsedAt: "2026-11-08T11:46:00Z" };

  function server(tokens: { status: number; body?: unknown }) {
    return signedIn()
      .on("GET", "/api/projects", { status: 200, body: [] })
      .on("GET", "/api/tokens", tokens)
      .on("GET", "/api/cli", {
        status: 200,
        body: {
          version: "0.2.0",
          installSh: "https://panopticon.team/install.sh",
          installPs1: "https://panopticon.team/install.ps1",
          platforms: {},
        },
      });
  }

  async function openWithGuide(fake: FakeServer, options: TestShellOptions = {}) {
    const shell = await startTestShell("/projects", fake, options);
    await until(() => shell.root.querySelector(".project-empty") !== null);
    await settle();
    return shell;
  }

  const guide = (root: HTMLElement) => root.querySelector(".projects-setup .setup-guide");

  it("New member: a member with no tokens sees the guide, above the list", async () => {
    const { root } = await openWithGuide(server({ status: 200, body: [] }));

    expect(guide(root)).not.toBeNull();
    const order = [...q(root, ".page").children].map((c) => c.className);
    expect(order.indexOf("projects-setup")).toBeLessThan(order.indexOf("project-list"));
    expect(root.querySelector(".projects-setup .setup-guide-hide")?.textContent).toBe("Hide");
  });

  it("a member logged in but not yet used still sees it, with install and log in done", async () => {
    const { root } = await openWithGuide(server({ status: 200, body: [unused] }));

    expect(
      [...root.querySelectorAll(".projects-setup .setup-step")].map((s) =>
        s.classList.contains("is-done"),
      ),
    ).toEqual([true, true, false]);
  });

  it("Hidden: Hide removes it, remembers that, and the next open doesn't show it", async () => {
    const { root, local } = await openWithGuide(server({ status: 200, body: [] }));

    q<HTMLButtonElement>(root, ".setup-guide-hide").click();

    expect(guide(root)).toBeNull();
    expect(local.getItem(SETUP_GUIDE_HIDDEN_KEY)).toBe("1");

    const again = server({ status: 200, body: [] });
    const reopened = await openWithGuide(again, { local: { [SETUP_GUIDE_HIDDEN_KEY]: "1" } });
    expect(guide(reopened.root)).toBeNull();
    expect(again.to("GET", "/api/tokens")).toHaveLength(0);
  });

  it("Member already set up: the Projects page doesn't show the guide", async () => {
    const { root } = await openWithGuide(server({ status: 200, body: [unused, reported] }));

    expect(guide(root)).toBeNull();
  });

  it("storage that throws still shows the guide, and Hide hides it for this page", async () => {
    const throwing = {
      getItem: () => {
        throw new Error("blocked");
      },
      setItem: () => {
        throw new Error("blocked");
      },
      removeItem: () => {
        throw new Error("blocked");
      },
    };
    const { root } = await openWithGuide(server({ status: 200, body: [] }), { storage: throwing });

    expect(guide(root)).not.toBeNull();
    q<HTMLButtonElement>(root, ".setup-guide-hide").click();
    expect(guide(root)).toBeNull();
  });

  it("a failed tokens request shows no guide, and the Projects list still shows", async () => {
    const { root } = await openWithGuide(server({ status: 500, body: { detail: "Boom." } }));

    expect(guide(root)).toBeNull();
    expect(root.querySelector(".project-empty")).not.toBeNull();
  });
});

// Spec projects, "Asking an agent to link a repo" (onboarding-guide D8).
describe("/projects/:id link hint", () => {
  it("Link hint: next to the .panopticon.json, it says to ask your agent to link the repo to PAN", async () => {
    const shell = await startTestShell(
      `/projects/${PAN_ID}`,
      signedIn().on("GET", PATH, { status: 200, body: details("viewer") }),
    );
    await until(() => shell.root.querySelector(".link-file") !== null);

    const hint = q(shell.root, ".link-repo .link-repo-agent");
    expect(hint.textContent).toBe(
      "Or ask your agent to link this repo to PAN: it can look the Project up and write the file.",
    );
    expect(hint.previousElementSibling?.classList.contains("copy-row")).toBe(true);
  });
});
