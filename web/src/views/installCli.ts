import { ApiError } from "../api";
import { button, el } from "../dom";
import type { Env } from "../shell";

/** `GET /api/cli` (server: Distribution/CliEndpoints; contract Panopticon.Contracts CliManifest). */
export interface CliManifest {
  version: string;
  installSh: string;
  installPs1: string;
  platforms: Record<string, { url: string; size: number; sha256: string }>;
}

type Os = "unix" | "windows";

/** Only for a 404 with no Problem `detail`, such as a proxy's: the server always sends its own message. */
const NO_BUILDS_FALLBACK =
  "This server has no CLI builds. Build the CLI from source instead: clone the repo and run make install.";

const LABELS: Record<Os, string> = { unix: "macOS and Linux", windows: "Windows" };

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

/**
 * The Tokens page's "Install the CLI" panel (design D8): the command for the visitor's OS, with the other OS one
 * click away, and a Copy button. A server without CLI builds says to build from source instead.
 */
export function installCliPanel(env: Env): { element: HTMLElement; load(): Promise<void> } {
  const body = el("div", "install-cli-body");
  const element = el(
    "section",
    "install-cli",
    el("h3", undefined, "Install the CLI"),
    el(
      "p",
      undefined,
      "Run this on a machine to install panopticon there, then log it in with ",
      el("code", undefined, "panopticon login"),
      ".",
    ),
    body,
  );

  async function load(): Promise<void> {
    let manifest: CliManifest;
    try {
      manifest = await env.api.get<CliManifest>("/api/cli");
    } catch (failure) {
      body.replaceChildren(
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

    render(manifest);
  }

  function render(manifest: CliManifest): void {
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
    body.replaceChildren(
      choices,
      command,
      el("div", "copy-row", copy, copied),
      el("p", "install-cli-version", `Installs panopticon ${manifest.version}.`),
    );
  }

  return { element, load };
}
