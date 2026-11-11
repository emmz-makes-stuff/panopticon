import { ApiError } from "../api";
import { button, el } from "../dom";
import type { Env } from "../shell";
import type { Token } from "./tokens";

/** `GET /api/cli` (server: Distribution/CliEndpoints; contract Panopticon.Contracts CliManifest). */
export interface CliManifest {
  version: string;
  installSh: string;
  installPs1: string;
  platforms: Record<string, { url: string; size: number; sha256: string }>;
}

type Os = "unix" | "windows";

const LABELS: Record<Os, string> = { unix: "macOS and Linux", windows: "Windows" };

/** Only for a 404 with no Problem `detail`, such as a proxy's: the server always sends its own message. */
const NO_BUILDS_FALLBACK =
  "This server has no CLI builds. Build the CLI from source instead: clone the repo and run make install.";

/**
 * The Claude Code plugin, from the public panopticon-skills marketplace (spec machine-setup, "Connect step for
 * Claude Code"; design D5). docs/cli.md gives the same commands.
 */
export const PLUGIN_COMMANDS = [
  "/plugin marketplace add emmz-makes-stuff/panopticon-skills",
  "/plugin install panopticon@panopticon-skills",
  "/panopticon:setup",
] as const;

/** The bridge as any MCP client starts it, as `.mcp.json` (the same as docs/cli.md and the plugin's own). */
export const MCP_JSON = `{
  "mcpServers": {
    "panopticon": {
      "type": "stdio",
      "command": "panopticon",
      "args": ["mcp"]
    }
  }
}`;

/** The one-line install command for each OS (spec cli-distribution, "Install hint on the Tokens page"). */
export function installCommand(manifest: CliManifest, os: Os): string {
  return os === "windows"
    ? `irm ${manifest.installPs1} | iex`
    : `curl -fsSL ${manifest.installSh} | sh`;
}

/** Windows gets PowerShell; everything else (macOS, Linux, ChromeOS, unknown) gets sh. */
export function defaultOs(platform: string): Os {
  return /^win/i.test(platform.trim()) ? "windows" : "unix";
}

/** The server as `panopticon login --server` takes it: the manifest's public URL, so it matches install.sh, or this page's origin without CLI builds (D2). */
export function serverUrl(manifest: CliManifest | null, fallback: string): string {
  return manifest ? manifest.installSh.replace(/\/install\.sh$/, "") : fallback.replace(/\/$/, "");
}

/** Which steps a member's own active tokens show as done (spec machine-setup, "Steps marked done"; D3). */
export function stepsDone(tokens: readonly Token[]): {
  install: boolean;
  login: boolean;
  connect: boolean;
} {
  const loggedIn = tokens.length > 0;
  return {
    install: loggedIn,
    login: loggedIn,
    connect: tokens.some((t) => t.lastUsedAt !== null),
  };
}

/** A command in a bordered block with its own Copy button; the prompt is drawn by CSS, so it never copies. */
function copyable(env: Env, text: string, prompt?: string): HTMLElement {
  const code = el("code", undefined, text);
  const block = el("pre", "setup-command", code);
  if (prompt) {
    block.dataset.prompt = prompt;
  }
  const copied = el("span", "copy-status");
  copied.setAttribute("role", "status");
  const copy = button("Copy", () => {
    env.copy(text).then(
      () => {
        copied.textContent = "Copied.";
      },
      () => {
        copied.textContent = "Couldn't copy. Select the text and copy it.";
      },
    );
  });
  return el("div", "setup-copyable", block, el("div", "copy-row", copy, copied));
}

export interface SetupGuide {
  element: HTMLElement;
  /** Fetches the manifest and renders the steps. */
  load(): Promise<void>;
  /** Marks the steps the member's tokens show as done. */
  setTokens(tokens: readonly Token[]): void;
}

/**
 * The machine setup guide (spec machine-setup; design D1): install the CLI, log in, connect your agent. The Tokens
 * page always shows it; the Projects page shows it until a token has reported, with `onHide` for its Hide button.
 */
export function setupGuide(env: Env, options: { onHide?: () => void } = {}): SetupGuide {
  const heading = el("div", "setup-guide-heading", el("h3", undefined, "Set up a machine"));
  if (options.onHide) {
    heading.append(button("Hide", options.onHide, "link-button setup-guide-hide"));
  }
  const steps = el("ol", "setup-steps");
  const element = el(
    "section",
    "setup-guide",
    heading,
    el(
      "p",
      "setup-guide-intro",
      "Run these on the machine where your agent works, then its reports show up here.",
    ),
    steps,
  );
  element.setAttribute("aria-label", "Set up a machine");

  const installBody = el("div", "install-cli");
  const loginBody = el("div", "setup-login");
  const connectBody = connectStep();
  const items = {
    install: step(1, "Install the CLI", installBody),
    login: step(2, "Log in", loginBody),
    connect: step(3, "Connect your agent", connectBody),
  };
  steps.append(items.install, items.login, items.connect);
  renderLogin(null);

  function step(number: number, title: string, body: HTMLElement): HTMLLIElement {
    const mark = el("span", "setup-step-mark", String(number));
    mark.setAttribute("aria-hidden", "true");
    const done = el("span", "setup-step-done", "Done");
    done.hidden = true;
    const item = el(
      "li",
      "setup-step",
      el("h4", "setup-step-title", mark, el("span", undefined, title), done),
      body,
    );
    item.dataset.number = String(number);
    return item;
  }

  function mark(item: HTMLLIElement, done: boolean): void {
    item.classList.toggle("is-done", done);
    const markEl = item.querySelector<HTMLElement>(".setup-step-mark");
    const doneEl = item.querySelector<HTMLElement>(".setup-step-done");
    if (markEl) {
      markEl.textContent = done ? "✓" : (item.dataset.number ?? "");
    }
    if (doneEl) {
      doneEl.hidden = !done;
    }
  }

  function renderLogin(manifest: CliManifest | null): void {
    const server = serverUrl(manifest, env.location.origin);
    loginBody.replaceChildren(
      el(
        "p",
        undefined,
        "Log the machine in to this server. It opens a page here to approve it, and saves the login.",
      ),
      copyable(env, `panopticon login --server ${server}`, "$"),
    );
  }

  function connectStep(): HTMLElement {
    const other = el(
      "details",
      "setup-other-agents",
      el("summary", undefined, "Other agents"),
      el(
        "p",
        undefined,
        "Any MCP client that can start a stdio server: run panopticon mcp in the repo. As .mcp.json:",
      ),
      copyable(env, MCP_JSON),
    );
    return el(
      "div",
      "setup-connect",
      el(
        "p",
        undefined,
        "In Claude Code, add the Panopticon plugin, then check the setup. The plugin uses this machine's saved login, so these are the same for every server.",
      ),
      ...PLUGIN_COMMANDS.map((command) => copyable(env, command, ">")),
      other,
    );
  }

  async function load(): Promise<void> {
    let manifest: CliManifest;
    try {
      manifest = await env.api.get<CliManifest>("/api/cli");
    } catch (failure) {
      installBody.replaceChildren(
        el(
          "p",
          "install-cli-note",
          failure instanceof ApiError && failure.status === 404
            ? // The server's own words (CliCatalogue.NoBuildsMessage), as the CLI and install scripts show them.
              failure.detail || NO_BUILDS_FALLBACK
            : "Couldn't load the install command. Reload the page to try again.",
        ),
      );
      return;
    }

    renderInstall(manifest);
    renderLogin(manifest);
  }

  function renderInstall(manifest: CliManifest): void {
    let os = defaultOs(env.platform());
    const code = el("code");
    const command = el("pre", "install-cli-command", code);
    const copied = el("span", "copy-status");
    copied.setAttribute("role", "status");
    const choices = el("div", "install-cli-os");
    choices.setAttribute("role", "group");
    choices.setAttribute("aria-label", "Operating system");
    const buttons = (["unix", "windows"] as const).map((option) => {
      const choice = button(LABELS[option], () => show(option), "install-cli-choice");
      choice.dataset.os = option;
      return choice;
    });
    choices.append(...buttons);

    // Switching OS changes the command in place, so the pressed button keeps focus.
    function show(next: Os): void {
      os = next;
      code.textContent = installCommand(manifest, os);
      // The prompt is drawn by CSS from this, so selecting the command never copies it.
      command.dataset.prompt = os === "windows" ? "PS>" : "$";
      copied.textContent = "";
      for (const choice of buttons) {
        choice.setAttribute("aria-pressed", String(choice.dataset.os === os));
      }
    }

    const copy = button("Copy", () => {
      env.copy(installCommand(manifest, os)).then(
        () => {
          copied.textContent = "Copied.";
        },
        () => {
          copied.textContent = "Couldn't copy. Select the text and copy it.";
        },
      );
    });
    show(os);
    installBody.replaceChildren(
      choices,
      command,
      el("div", "copy-row", copy, copied),
      el("p", "install-cli-version", `Installs panopticon ${manifest.version}.`),
    );
  }

  function setTokens(tokens: readonly Token[]): void {
    const done = stepsDone(tokens);
    mark(items.install, done.install);
    mark(items.login, done.login);
    mark(items.connect, done.connect);
  }

  setTokens([]);
  return { element, load, setTokens };
}
