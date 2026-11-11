import { ApiError } from "../api";
import { alertLine, button, el, page } from "../dom";
import { calendarDate, relativeTime } from "../format";
import type { View } from "../router";
import type { Env } from "../shell";
import { installCliPanel } from "./installCli";

/** One of your API tokens (server: Auth/TokenEndpoints TokenView). There's never a secret in it. */
export interface Token {
  id: string;
  machineName: string;
  /** ISO 8601. */
  createdAt: string;
  /** ISO 8601, or null if it hasn't reported yet. */
  lastUsedAt: string | null;
}

/** `/tokens` (spec reporter-credentials, "Tokens page"): your machines' tokens, and revoking them. */
export function tokensView(env: Env): View {
  return async ({ root }) => {
    const list = el("ul", "token-list");
    const error = alertLine();
    const install = installCliPanel(env);
    const status = el("p", "form-status");
    status.setAttribute("role", "status");

    async function load(): Promise<void> {
      try {
        const tokens = await env.api.get<Token[]>("/api/tokens");
        list.replaceChildren(
          ...(tokens.length > 0
            ? tokens.map(row)
            : [
                el(
                  "li",
                  "token-empty",
                  "No tokens yet. Run panopticon login on a machine to add one.",
                ),
              ]),
        );
        error.clear();
      } catch (failure) {
        error.show(
          failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.",
        );
      }
    }

    function row(token: Token): HTMLLIElement {
      const used = token.lastUsedAt
        ? `Last used ${relativeTime(token.lastUsedAt, env.now())}`
        : "Not used yet";
      return el(
        "li",
        "token",
        el("span", "token-machine", token.machineName),
        el(
          "span",
          "token-meta",
          el("span", undefined, `Created ${calendarDate(token.createdAt)}`),
          el("span", undefined, used),
        ),
        button(
          "Revoke",
          () => {
            if (
              !env.confirm(`Revoke the token for ${token.machineName}? Its next report is refused.`)
            ) {
              return;
            }

            env.api.send("DELETE", `/api/tokens/${encodeURIComponent(token.id)}`).then(
              async () => {
                status.textContent = `Revoked the token for ${token.machineName}.`;
                await load();
              },
              (failure: unknown) =>
                error.show(
                  failure instanceof ApiError
                    ? failure.message
                    : "Couldn't reach the server. Try again.",
                ),
            );
          },
          "danger-button",
        ),
      );
    }

    root.replaceChildren(
      page(
        "Tokens",
        el(
          "p",
          undefined,
          "Each machine you log in with panopticon login has its own token. Revoke one you no longer use.",
        ),
        status,
        error.element,
        list,
        install.element,
      ),
    );
    await Promise.all([load(), install.load()]);
  };
}
