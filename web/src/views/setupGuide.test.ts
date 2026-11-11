import { describe, expect, it, vi } from "vitest";
import { q, until } from "../testing";
import { signedIn, startTestShell } from "../testShell";
import { MCP_JSON, PLUGIN_COMMANDS, serverUrl, stepsDone, type CliManifest } from "./setupGuide";
import type { Token } from "./tokens";

const reported: Token = {
  id: "t1",
  machineName: "vm-1",
  createdAt: "2026-11-01T09:00:00Z",
  lastUsedAt: "2026-11-08T11:46:00Z",
};
const fresh: Token = {
  id: "t2",
  machineName: "laptop",
  createdAt: "2026-11-08T08:00:00Z",
  lastUsedAt: null,
};

function manifestFor(server: string): CliManifest {
  return {
    version: "0.2.0",
    installSh: `${server}/install.sh`,
    installPs1: `${server}/install.ps1`,
    platforms: {},
  };
}

async function openTokens(
  tokens: () => Token[],
  options: {
    manifest?: { status: number; body?: unknown };
    copy?: (text: string) => Promise<void>;
    onDelete?: () => void;
  } = {},
) {
  const server = signedIn()
    .on("GET", "/api/tokens", () => ({ status: 200, body: tokens() }))
    .on(
      "GET",
      "/api/cli",
      options.manifest ?? { status: 200, body: manifestFor("https://panopticon.team") },
    )
    .on("DELETE", "/api/tokens/t1", () => {
      options.onDelete?.();
      return { status: 204 };
    });
  const shell = await startTestShell("/tokens", server, options.copy ? { copy: options.copy } : {});
  await until(
    () =>
      shell.root.querySelector(".install-cli-command, .install-cli-note") !== null &&
      shell.root.querySelectorAll(".token-list li").length > 0,
  );
  return shell;
}

const titles = (root: HTMLElement) =>
  [
    ...root.querySelectorAll(
      ".setup-step-title > span:not(.setup-step-mark):not(.setup-step-done)",
    ),
  ].map((t) => t.textContent);
const done = (root: HTMLElement) =>
  [...root.querySelectorAll<HTMLElement>(".setup-step")].map((s) =>
    s.classList.contains("is-done"),
  );
const commands = (root: HTMLElement, within: string) =>
  [...root.querySelectorAll(`${within} .setup-command code`)].map((c) => c.textContent);

// Spec machine-setup (onboarding-guide).
describe("setup guide", () => {
  it("Three setup steps: install, log in with this server, connect, in order", async () => {
    const { root } = await openTokens(() => []);

    expect(titles(root)).toEqual(["Install the CLI", "Log in", "Connect your agent"]);
    expect(commands(root, ".setup-login")).toEqual([
      "panopticon login --server https://panopticon.team",
    ]);
    expect(root.querySelector(".setup-guide-intro")?.textContent).toContain(
      "the machine where your agent works",
    );
    expect(root.querySelector(".setup-guide")?.textContent).not.toContain(".panopticon.json");
  });

  it("Self-hosted server: the log-in command names it", async () => {
    const { root } = await openTokens(() => [], {
      manifest: { status: 200, body: manifestFor("https://panopticon.example.com") },
    });

    expect(commands(root, ".setup-login")).toEqual([
      "panopticon login --server https://panopticon.example.com",
    ]);
  });

  it("Server without CLI builds: install says so, and the other steps still show", async () => {
    const { root } = await openTokens(() => [], {
      manifest: {
        status: 404,
        body: { detail: "This server has no CLI builds. Run make install." },
      },
    });

    expect(root.querySelector(".install-cli-note")?.textContent).toContain("no CLI builds");
    // No manifest: the server is this page's own origin.
    expect(commands(root, ".setup-login")).toEqual([
      "panopticon login --server https://panopticon.test",
    ]);
    expect(commands(root, ".setup-connect")).toContain(PLUGIN_COMMANDS[0]);
  });

  it("Plugin commands: marketplace, install and setup, each copied exactly", async () => {
    const copy = vi.fn(() => Promise.resolve());
    const { root } = await openTokens(() => [], { copy });
    const connect = q(root, ".setup-connect");

    expect(commands(root, ".setup-connect > .setup-copyable")).toEqual([...PLUGIN_COMMANDS]);
    expect(connect.textContent).toContain("saved login");
    const buttons = [
      ...connect.querySelectorAll<HTMLButtonElement>(":scope > .setup-copyable button"),
    ];
    for (const b of buttons) {
      b.click();
    }
    await until(() => copy.mock.calls.length === 3);

    expect(copy.mock.calls.map((c: unknown[]) => c[0])).toEqual([...PLUGIN_COMMANDS]);
  });

  it("Another agent: the .mcp.json behind one click, copied exactly", async () => {
    const copy = vi.fn(() => Promise.resolve());
    const { root } = await openTokens(() => [], { copy });
    const other = q<HTMLDetailsElement>(root, ".setup-other-agents");

    expect(other.open).toBe(false);
    expect(other.querySelector("summary")?.textContent).toBe("Other agents");
    expect(commands(root, ".setup-other-agents")).toEqual([MCP_JSON]);
    expect(JSON.parse(MCP_JSON)).toEqual({
      mcpServers: { panopticon: { type: "stdio", command: "panopticon", args: ["mcp"] } },
    });
    q<HTMLButtonElement>(other, "button").click();
    await until(() => copy.mock.calls.length === 1);

    expect(copy).toHaveBeenCalledWith(MCP_JSON);
  });

  it("the log-in command copies too", async () => {
    const copy = vi.fn(() => Promise.resolve());
    const { root } = await openTokens(() => [], { copy });

    q<HTMLButtonElement>(root, ".setup-login button").click();
    await until(() => copy.mock.calls.length === 1);

    expect(copy).toHaveBeenCalledWith("panopticon login --server https://panopticon.team");
  });

  it("Steps marked done: none, then install and log in, then all three", () => {
    expect(stepsDone([])).toEqual({ install: false, login: false, connect: false });
    expect(stepsDone([fresh])).toEqual({ install: true, login: true, connect: false });
    expect(stepsDone([fresh, reported])).toEqual({ install: true, login: true, connect: true });
  });

  it("No tokens: no step is marked done", async () => {
    const { root } = await openTokens(() => []);

    expect(done(root)).toEqual([false, false, false]);
    expect([...root.querySelectorAll(".setup-step-mark")].map((m) => m.textContent)).toEqual([
      "1",
      "2",
      "3",
    ]);
  });

  it("Logged in, not yet reported: install and log in are done, connect isn't", async () => {
    const { root } = await openTokens(() => [fresh]);

    expect(done(root)).toEqual([true, true, false]);
    expect(
      [...root.querySelectorAll<HTMLElement>(".setup-step-done")].map((d) => d.hidden),
    ).toEqual([false, false, true]);
  });

  it("Reported: all three are done, and their commands still show (Guide on the Tokens page)", async () => {
    const { root } = await openTokens(() => [reported]);

    expect(done(root)).toEqual([true, true, true]);
    expect(root.querySelector(".install-cli-command")).not.toBeNull();
    expect(commands(root, ".setup-connect > .setup-copyable")).toEqual([...PLUGIN_COMMANDS]);
  });

  it("revoking the only reported token un-marks connect", async () => {
    let tokens = [reported, fresh];
    const { root } = await openTokens(() => tokens, {
      onDelete: () => {
        tokens = [fresh];
      },
    });
    expect(done(root)).toEqual([true, true, true]);

    q<HTMLButtonElement>(root, ".token .danger-button").click();
    await until(() => root.querySelectorAll(".token-list li").length === 1);

    expect(done(root)).toEqual([true, true, false]);
  });

  it("serverUrl drops install.sh, and a trailing slash from the fallback", () => {
    expect(serverUrl(manifestFor("https://x.example/base"), "https://ignored")).toBe(
      "https://x.example/base",
    );
    expect(serverUrl(null, "https://panopticon.test/")).toBe("https://panopticon.test");
  });
});
