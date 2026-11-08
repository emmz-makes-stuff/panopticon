import { describe, expect, it } from "vitest";
import { RETURN_TO_KEY } from "../shell";
import { MEMBER, until, type FakeServer, q } from "../testing";
import { signedIn, signedOut, startTestShell } from "../testShell";
import type { DeviceLogin } from "./device";

const pending: DeviceLogin = { userCode: "BCDF-GHJK", machineName: "vm-1", state: "pending" };

const withLogin = (login: DeviceLogin | null): FakeServer =>
  signedIn().on(
    "GET",
    "/api/device/BCDF-GHJK",
    login ? { status: 200, body: login } : { status: 404, body: {} },
  );

async function open(server: FakeServer, url = "/device?code=BCDF-GHJK") {
  const shell = await startTestShell(url, server);
  await until(() => shell.heading() !== undefined && shell.heading() !== "");
  return shell;
}

const press = (root: HTMLElement, text: string) => {
  const found = [...root.querySelectorAll<HTMLButtonElement>(".page button")].find(
    (b) => b.textContent === text,
  );
  if (!found) {
    throw new Error(`No button "${text}"`);
  }

  found.click();
};

// Spec reporter-credentials: "Approving a device login" (the dashboard side).
describe("/device", () => {
  it("shows the machine name and the code, and approves nothing on load", async () => {
    const server = withLogin(pending);

    const { heading, root } = await open(server);

    expect(heading()).toBe("Log in vm-1?");
    expect(root.querySelector(".device-code")?.textContent).toBe("Code BCDF-GHJK");
    expect(root.querySelector(".page")?.textContent).toContain(`reports as ${MEMBER.displayName}`);
    expect(server.calls.filter((call) => call.method === "POST")).toHaveLength(0);
  });

  it("Approve: posts the approval and says to go back to the terminal", async () => {
    const server = withLogin(pending).on("POST", "/api/device/BCDF-GHJK/approve", { status: 204 });
    const { heading, root } = await open(server);

    press(root, "Approve");
    await until(() => heading() === "Approved");

    expect(server.to("POST", "/api/device/BCDF-GHJK/approve")).toHaveLength(1);
    expect(root.querySelector(".page p")?.textContent).toContain("vm-1 is logged in as you");
  });

  it("Deny: posts the denial and says no token was issued", async () => {
    const server = withLogin(pending).on("POST", "/api/device/BCDF-GHJK/deny", { status: 204 });
    const { heading, root } = await open(server);

    press(root, "Deny");
    await until(() => heading() === "Denied");

    expect(server.to("POST", "/api/device/BCDF-GHJK/deny")).toHaveLength(1);
    expect(server.to("POST", "/api/device/BCDF-GHJK/approve")).toHaveLength(0);
    expect(root.querySelector(".page p")?.textContent).toBe("No token was issued to vm-1.");
  });

  it.each<[string, DeviceLogin | null]>([
    ["unknown", null],
    ["expired", { ...pending, state: "expired" }],
    ["already used", { ...pending, state: "approved" }],
  ])(
    "Unknown or expired code (%s): says it isn't valid, offers another, and approves nothing",
    async (_case, login) => {
      const server = withLogin(login);

      const { heading, root } = await open(server);

      expect(heading()).toBe("Log in a machine");
      expect(root.querySelector(".form-error")?.textContent).toContain("That code isn't valid");
      expect(root.querySelector("input[name=code]")).not.toBeNull();
      expect(server.calls.filter((call) => call.method === "POST")).toHaveLength(0);
    },
  );

  it("a code that stops being valid before approval says so", async () => {
    const server = withLogin(pending).on("POST", "/api/device/BCDF-GHJK/approve", {
      status: 404,
      body: {},
    });
    const { heading, root } = await open(server);

    press(root, "Approve");
    await until(() => heading() === "Log in a machine");

    expect(root.querySelector(".form-error")?.textContent).toContain("That code isn't valid");
  });

  it("without a code, asks for one and goes to its page", async () => {
    const server = withLogin(pending);
    const { root, nav, heading } = await open(server, "/device");
    expect(heading()).toBe("Log in a machine");

    q<HTMLInputElement>(root, "input[name=code]").value = " BCDF-GHJK ";
    q(root, "form").dispatchEvent(new Event("submit", { cancelable: true }));
    await until(() => heading() === "Log in vm-1?");

    expect(nav.path()).toBe("/device?code=BCDF-GHJK");
  });

  it("Not signed in: asks to sign in first, then comes back to the same code", async () => {
    const server = signedOut();
    const first = await startTestShell("/device?code=BCDF-GHJK", server);

    expect(first.heading()).toBe("Sign in");
    expect(first.session.getItem(RETURN_TO_KEY)).toBe("/device?code=BCDF-GHJK");
    expect(server.calls.some((call) => call.path.startsWith("/api/device"))).toBe(false);

    // The magic link opens in the same tab: the return path survives in sessionStorage.
    server
      .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
      .on("GET", "/api/device/BCDF-GHJK", { status: 200, body: pending });
    const back = await startTestShell("/sign-in/confirm#secret", server, {
      session: { [RETURN_TO_KEY]: "/device?code=BCDF-GHJK" },
    });
    q<HTMLButtonElement>(back.root, ".page button").click();
    await until(() => back.heading() === "Log in vm-1?");

    expect(back.nav.path()).toBe("/device?code=BCDF-GHJK");
    expect(server.to("POST", "/api/device/BCDF-GHJK/approve")).toHaveLength(0);
  });
});
