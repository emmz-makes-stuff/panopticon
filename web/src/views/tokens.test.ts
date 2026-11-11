import { describe, expect, it, vi } from "vitest";
import { until, type FakeServer, q } from "../testing";
import { signedIn, startTestShell } from "../testShell";
import type { Token } from "./tokens";
import type { CliManifest } from "./installCli";

const vm1: Token = {
  id: "t1",
  machineName: "vm-1",
  createdAt: "2026-11-01T09:00:00Z",
  lastUsedAt: "2026-11-08T11:46:00Z",
};
const laptop: Token = {
  id: "t2",
  machineName: "laptop",
  createdAt: "2026-11-08T08:00:00Z",
  lastUsedAt: null,
};

async function open(server: FakeServer, confirm?: (message: string) => boolean) {
  const shell = await startTestShell("/tokens", server, confirm ? { confirm } : {});
  await until(() => shell.root.querySelectorAll(".token-list li").length > 0);
  return shell;
}

const machines = (root: HTMLElement) =>
  [...root.querySelectorAll(".token-machine")].map((m) => m.textContent);

// Spec reporter-credentials: "Tokens page" (the dashboard side), "Token never shown again".
describe("/tokens", () => {
  it("lists your tokens with machine, created and last used", async () => {
    const { root, heading } = await open(
      signedIn().on("GET", "/api/tokens", { status: 200, body: [vm1, laptop] }),
    );

    expect(heading()).toBe("Tokens");
    expect(machines(root)).toEqual(["vm-1", "laptop"]);
    const meta = [...root.querySelectorAll(".token-meta")].map((m) => m.textContent);
    expect(meta).toEqual(["Created 1 Nov 2026Last used 14m ago", "Created 8 Nov 2026Not used yet"]);
  });

  it("says how to add one when there are none", async () => {
    const { root } = await open(signedIn().on("GET", "/api/tokens", { status: 200, body: [] }));

    expect(root.querySelector(".token-empty")?.textContent).toContain("panopticon login");
  });

  it("List and revoke: asks first, then revokes it and it leaves the list", async () => {
    let tokens = [vm1, laptop];
    const server = signedIn()
      .on("GET", "/api/tokens", () => ({ status: 200, body: tokens }))
      .on("DELETE", "/api/tokens/t1", () => {
        tokens = [laptop];
        return { status: 204 };
      });
    const confirm = vi.fn((message: string) => message.length > 0);
    const { root } = await open(server, confirm);

    q<HTMLButtonElement>(root, ".token .danger-button").click();
    await until(() => machines(root).length === 1);

    expect(confirm.mock.calls[0]?.[0]).toContain("Revoke the token for vm-1?");
    expect(server.to("DELETE", "/api/tokens/t1")).toHaveLength(1);
    expect(machines(root)).toEqual(["laptop"]);
    expect(root.querySelector(".form-status")?.textContent).toBe("Revoked the token for vm-1.");
  });

  it("cancelling the confirmation revokes nothing", async () => {
    const server = signedIn().on("GET", "/api/tokens", { status: 200, body: [vm1] });
    const { root } = await open(server, () => false);

    q<HTMLButtonElement>(root, ".token .danger-button").click();

    expect(server.to("DELETE", "/api/tokens/t1")).toHaveLength(0);
  });
});

const manifest: CliManifest = {
  version: "0.2.0",
  installSh: "https://panopticon.team/install.sh",
  installPs1: "https://panopticon.team/install.ps1",
  platforms: {
    "linux-x64": { url: "https://panopticon.team/cli/linux-x64/panopticon", size: 1, sha256: "a" },
  },
};
const SH = "curl -fsSL https://panopticon.team/install.sh | sh";
const PS = "irm https://panopticon.team/install.ps1 | iex";

async function openInstall(
  options: { platform?: string; copy?: (text: string) => Promise<void> } = {},
  answer: { status: number; body?: unknown } = { status: 200, body: manifest },
) {
  const server = signedIn()
    .on("GET", "/api/tokens", { status: 200, body: [vm1] })
    .on("GET", "/api/cli", answer);
  const shell = await startTestShell("/tokens", server, options);
  await until(
    () =>
      shell.root.querySelector(".install-cli-command, .install-cli-note") !== null &&
      shell.root.querySelectorAll(".token-list li").length > 0,
  );
  return shell;
}

const command = (root: HTMLElement) => root.querySelector(".install-cli-command")?.textContent;
const pressed = (root: HTMLElement) =>
  [...root.querySelectorAll(".install-cli-choice[aria-pressed='true']")].map((b) => b.textContent);
function choice(root: HTMLElement, label: string): HTMLButtonElement {
  const found = [...root.querySelectorAll<HTMLButtonElement>(".install-cli-choice")].find(
    (b) => b.textContent === label,
  );
  if (!found) {
    throw new Error(`No "${label}" button`);
  }
  return found;
}

// Spec cli-distribution: "Install hint on the Tokens page" (design D8).
describe("/tokens install panel", () => {
  it("shows the sh command to a visitor on macOS, with its version", async () => {
    const { root } = await openInstall({ platform: "macOS" });

    expect(root.querySelector(".install-cli h3")?.textContent).toBe("Install the CLI");
    expect(command(root)).toBe(SH);
    expect(pressed(root)).toEqual(["macOS and Linux"]);
    expect(root.querySelector(".install-cli-version")?.textContent).toBe(
      "Installs panopticon 0.2.0.",
    );
  });

  it.each(["Windows", "Win32"])(
    "shows the PowerShell command when the browser says %s",
    async (platform) => {
      const { root } = await openInstall({ platform });

      expect(command(root)).toBe(PS);
      expect(pressed(root)).toEqual(["Windows"]);
    },
  );

  it.each(["Linux", "Linux x86_64", "Chrome OS", ""])("defaults to sh for %j", async (platform) => {
    const { root } = await openInstall({ platform });

    expect(command(root)).toBe(SH);
  });

  it("switches OS in place, keeping focus on the pressed button", async () => {
    const { root, events } = await openInstall({ platform: "macOS" });
    document.body.append(events);
    const windows = choice(root, "Windows");

    windows.focus();
    windows.click();

    expect(command(root)).toBe(PS);
    expect(pressed(root)).toEqual(["Windows"]);
    expect(document.activeElement).toBe(windows);

    expect(root.querySelector<HTMLElement>(".install-cli-command")?.dataset.prompt).toBe("PS>");

    choice(root, "macOS and Linux").click();
    expect(command(root)).toBe(SH);
    expect(root.querySelector<HTMLElement>(".install-cli-command")?.dataset.prompt).toBe("$");
    events.remove();
  });

  it("Copy puts the shown command on the clipboard", async () => {
    const copy = vi.fn(() => Promise.resolve());
    const { root } = await openInstall({ platform: "macOS", copy });

    choice(root, "Windows").click();
    q<HTMLButtonElement>(root, ".install-cli .copy-row button").click();
    await until(() => root.querySelector(".install-cli .copy-status")?.textContent === "Copied.");

    expect(copy).toHaveBeenCalledWith(PS);
  });

  it("says to select the text when the clipboard refuses", async () => {
    const { root } = await openInstall({ copy: () => Promise.reject(new Error("denied")) });

    q<HTMLButtonElement>(root, ".install-cli .copy-row button").click();

    await until(() => root.querySelector(".install-cli .copy-status")?.textContent !== "");
    expect(root.querySelector(".install-cli .copy-status")?.textContent).toBe(
      "Couldn't copy. Select the text and copy it.",
    );
  });

  it("a server without CLI builds points to make install and shows no command", async () => {
    const { root } = await openInstall(
      {},
      {
        status: 404,
        body: {
          detail:
            "This server has no CLI builds. Build the CLI from source instead: clone the repo and run make install.",
        },
      },
    );

    expect(root.querySelector(".install-cli-note")?.textContent).toContain("make install");
    expect(root.querySelector(".install-cli-note")?.textContent).toContain("no CLI builds");
    expect(command(root)).toBeUndefined();
    expect(machines(root)).toEqual(["vm-1"]);
  });

  it("the no-builds note is the server's own message", async () => {
    const { root } = await openInstall(
      {},
      { status: 404, body: { detail: "No CLI builds here: see docs/cli.md." } },
    );

    expect(root.querySelector(".install-cli-note")?.textContent).toBe(
      "No CLI builds here: see docs/cli.md.",
    );
    expect(command(root)).toBeUndefined();
  });

  it("a 404 without a detail still points to make install", async () => {
    const { root } = await openInstall({}, { status: 404, body: {} });

    expect(root.querySelector(".install-cli-note")?.textContent).toContain("make install");
  });

  it("a failed manifest request says to reload, and the tokens still show", async () => {
    const { root } = await openInstall({}, { status: 500, body: { detail: "Boom." } });

    expect(root.querySelector(".install-cli-note")?.textContent).toContain("Reload");
    expect(machines(root)).toEqual(["vm-1"]);
  });
});
