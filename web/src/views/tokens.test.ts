import { describe, expect, it, vi } from "vitest";
import { until, type FakeServer, q } from "../testing";
import { signedIn, startTestShell } from "../testShell";
import type { Token } from "./tokens";

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
