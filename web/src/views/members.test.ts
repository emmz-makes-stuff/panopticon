import { describe, expect, it, vi } from "vitest";
import { MEMBER, until, type FakeServer, q } from "../testing";
import { signedIn, startTestShell } from "../testShell";
import type { Member } from "./members";

const me: Member = { ...MEMBER, joinedAt: "2026-11-01T09:00:00Z" };
const sam: Member = {
  id: "m2",
  email: "sam@example.com",
  displayName: "Sam",
  joinedAt: "2026-11-08T10:00:00Z",
};

function withMembers(members: Member[]): FakeServer {
  return signedIn().on("GET", "/api/members", { status: 200, body: members });
}

const rows = (root: HTMLElement) =>
  [...root.querySelectorAll(".member")].map((row) => ({
    name: row.querySelector(".member-name")?.textContent,
    email: row.querySelector(".member-email")?.textContent,
    joined: row.querySelector(".member-joined")?.textContent,
    remove: row.querySelector(".danger-button") !== null,
    rename: row.querySelector(".rename-form") !== null,
  }));

async function open(server: FakeServer, confirm?: (message: string) => boolean) {
  const shell = await startTestShell("/members", server, confirm ? { confirm } : {});
  await until(() => shell.root.querySelectorAll(".member").length > 0);
  return shell;
}

function submit(form: Element | null) {
  if (!form) {
    throw new Error("No form");
  }

  form.dispatchEvent(new Event("submit", { cancelable: true }));
}

// Spec team-members: "Member list", "Display names", "Removing a member".
describe("/members", () => {
  it("List members: each with email, display name and join date; rename for you, remove for others", async () => {
    const { root, heading } = await open(withMembers([me, sam]));

    expect(heading()).toBe("Members");
    expect(rows(root)).toEqual([
      {
        name: "Emmz (you)",
        email: "emmz@example.com",
        joined: "Joined 1 Nov 2026",
        remove: false,
        rename: true,
      },
      {
        name: "Sam",
        email: "sam@example.com",
        joined: "Joined 8 Nov 2026",
        remove: true,
        rename: false,
      },
    ]);
  });

  it("shows a display name with markup as text", async () => {
    const { root } = await open(
      withMembers([me, { ...sam, displayName: "<b>Sam</b><script>x()</script>" }]),
    );

    const name = q(root, ".member:nth-child(2) .member-name");
    expect(name.textContent).toBe("<b>Sam</b><script>x()</script>");
    expect(name.querySelector("b, script")).toBeNull();
  });

  it("has no invite form, and points to Projects for inviting", async () => {
    const { root } = await open(withMembers([me, sam]));

    expect(root.querySelector(".invite-form")).toBeNull();
    const hint = q(root, ".members-invite-hint");
    expect(hint.textContent).toBe(
      "To invite someone, open one of your Projects. Its owners can invite people from there.",
    );
    expect(hint.querySelector("a")?.getAttribute("href")).toBe("/projects");
  });

  it("Only initial members remove: without canRemoveMembers, nobody has a Remove button", async () => {
    const { root } = await open(
      withMembers([me, sam]).on("GET", "/api/session", {
        status: 200,
        body: { ...MEMBER, canRemoveMembers: false },
      }),
    );

    expect(rows(root).map((row) => row.remove)).toEqual([false, false]);
  });

  it("Last owner removed: shows the server's refusal naming the Project", async () => {
    const detail = "Sam is the only owner of ACME. Make someone else its owner first.";
    const server = withMembers([me, sam]).on("DELETE", "/api/members/m2", {
      status: 409,
      body: { detail },
    });
    const { root } = await open(server);

    q<HTMLButtonElement>(root, ".danger-button").click();
    await until(() => !!root.querySelector(".page > .form-error")?.textContent);

    expect(root.querySelector(".page > .form-error")?.textContent).toBe(detail);
    expect(rows(root)).toHaveLength(2);
  });

  it("Rename yourself: saves the trimmed name, and the list and header show it", async () => {
    let members = [me];
    const server = signedIn()
      .on("GET", "/api/members", () => ({ status: 200, body: members }))
      .on("PATCH", "/api/members/me", (call) => {
        members = [{ ...me, displayName: (call.body as { displayName: string }).displayName }];
        return { status: 200, body: members[0] };
      });
    const { root } = await open(server);
    q<HTMLInputElement>(root, ".rename-form input").value = "  Emmz R ";

    submit(root.querySelector(".rename-form"));
    await until(() => rows(root)[0]?.name === "Emmz R (you)");

    expect(server.to("PATCH", "/api/members/me")[0]?.body).toEqual({ displayName: "Emmz R" });
    expect(root.querySelector(".account-name")?.textContent).toBe("Emmz R");
  });

  it("Blank name rejected: nothing is sent and the name stays", async () => {
    const server = withMembers([me]);
    const { root } = await open(server);
    q<HTMLInputElement>(root, ".rename-form input").value = "   ";

    submit(root.querySelector(".rename-form"));

    expect(server.to("PATCH", "/api/members/me")).toHaveLength(0);
    expect(root.querySelector(".rename-form .form-error")?.textContent).toContain(
      "1 to 50 characters",
    );
    expect(rows(root)[0]?.name).toBe("Emmz (you)");
  });

  it("Remove a colleague: asks first, then removes them from the list", async () => {
    let members = [me, sam];
    const server = signedIn()
      .on("GET", "/api/members", () => ({ status: 200, body: members }))
      .on("DELETE", "/api/members/m2", () => {
        members = [me];
        return { status: 204 };
      });
    const confirm = vi.fn((message: string) => message.length > 0);
    const { root } = await open(server, confirm);

    q<HTMLButtonElement>(root, ".danger-button").click();
    await until(() => rows(root).length === 1);

    expect(confirm.mock.calls[0]?.[0]).toContain("Remove Sam (sam@example.com) from the server?");
    expect(server.to("DELETE", "/api/members/m2")).toHaveLength(1);
  });

  it("cancelling the confirmation removes nobody", async () => {
    const server = withMembers([me, sam]);
    const { root } = await open(server, () => false);

    q<HTMLButtonElement>(root, ".danger-button").click();

    expect(server.to("DELETE", "/api/members/m2")).toHaveLength(0);
    expect(rows(root)).toHaveLength(2);
  });
});
