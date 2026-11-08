import { ApiError } from "../api";
import { alertLine, button, el, field, page } from "../dom";
import type { View } from "../router";
import type { Env } from "../shell";

/** A device login as the approval page sees it (server: Auth/DeviceEndpoints DeviceLoginView). */
export interface DeviceLogin {
  userCode: string;
  machineName: string;
  state: "pending" | "approved" | "denied" | "expired";
}

const INVALID =
  "That code isn't valid. It may have expired or been used already: run panopticon login again for a new one.";

/**
 * `/device?code=XXXX-XXXX` (spec reporter-credentials, "Approving a device login"). It shows the machine the code is
 * for, and approves or denies only when the member presses a button: never on load. Signed out, the shell sends you
 * through sign-in and back here with the same code.
 */
export function deviceView(env: Env): View {
  return async ({ root, params, navigate }) => {
    function codeForm(message?: string): void {
      const { element, input } = field("Code", {
        name: "code",
        autocomplete: "off",
        required: true,
      });
      const go = el("button", "primary", "Continue");
      go.type = "submit";
      const error = alertLine();
      if (message) {
        error.show(message);
      }

      const form = el("form", "code-form", element, go, error.element);
      form.noValidate = true;
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const code = input.value.trim();
        if (code) {
          navigate(`/device?code=${encodeURIComponent(code)}`);
        } else {
          error.show("Enter the code panopticon login shows.");
        }
      });
      root.replaceChildren(
        page(
          "Log in a machine",
          el("p", undefined, "Enter the code that panopticon login shows in the terminal."),
          form,
        ),
      );
      input.focus();
    }

    function done(title: string, text: string): void {
      root.replaceChildren(page(title, el("p", undefined, text)));
    }

    const code = params.get("code")?.trim();
    if (!code) {
      codeForm();
      return;
    }

    let login: DeviceLogin;
    try {
      login = await env.api.get<DeviceLogin>(`/api/device/${encodeURIComponent(code)}`);
    } catch (failure) {
      codeForm(failure instanceof ApiError && failure.status !== 404 ? failure.message : INVALID);
      return;
    }

    if (login.state !== "pending") {
      codeForm(INVALID);
      return;
    }

    const target = code;
    const error = alertLine();
    function decide(decision: "approve" | "deny"): void {
      approve.disabled = true;
      deny.disabled = true;
      error.clear();
      env.api.send("POST", `/api/device/${encodeURIComponent(target)}/${decision}`).then(
        () =>
          decision === "approve"
            ? done("Approved", `${login.machineName} is logged in as you. Go back to its terminal.`)
            : done("Denied", `No token was issued to ${login.machineName}.`),
        (failure: unknown) => {
          if (failure instanceof ApiError && failure.status === 404) {
            codeForm(INVALID);
            return;
          }

          approve.disabled = false;
          deny.disabled = false;
          error.show(
            failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.",
          );
        },
      );
    }

    const approve = button("Approve", () => decide("approve"), "primary");
    const deny = button("Deny", () => decide("deny"));
    const name = env.member()?.displayName ?? "you";
    root.replaceChildren(
      page(
        `Log in ${login.machineName}?`,
        el("p", "device-code", `Code ${login.userCode}`),
        el(
          "p",
          undefined,
          `This gives ${login.machineName} an API token that reports as ${name}. Only approve a code you started yourself with panopticon login.`,
        ),
        el("div", "actions", approve, deny),
        error.element,
      ),
    );
  };
}
