import { ApiError, type SessionMember } from "../api";
import { alertLine, button, el, field, page, routeLink } from "../dom";
import type { Env } from "../shell";
import type { View } from "../router";

/** The address this browser last asked for a link for, so the confirm page can say who it signs in. */
export const SIGN_IN_EMAIL_KEY = "panopticon.signInEmail";

/** How long a sign-in link lasts (server: SignInEndpoints.LinkLifetime). */
const LINK_MINUTES = 15;

/**
 * `/sign-in`: ask for a magic link (spec dashboard-sign-in, "Requesting a sign-in link"). The answer reads the same
 * whether or not the address is a member's.
 */
export function signInView(env: Env): View {
  return ({ root }) => {
    function showForm(email = ""): void {
      const { element: emailField, input } = field("Email", {
        type: "email",
        name: "email",
        autocomplete: "email",
        required: true,
        value: email,
      });
      const submit = el("button", "primary", "Send me a link");
      submit.type = "submit";
      const error = alertLine();
      const form = el("form", "sign-in-form", emailField, submit, error.element);
      form.noValidate = true;
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const address = input.value.trim();
        if (!address) {
          error.show("Enter your email address.");
          return;
        }

        error.clear();
        submit.disabled = true;
        env.api.send("POST", "/api/auth/sign-in", { email: address }).then(
          () => {
            env.local.setItem(SIGN_IN_EMAIL_KEY, address);
            showSent(address);
          },
          (failure: unknown) => {
            submit.disabled = false;
            error.show(
              failure instanceof ApiError
                ? failure.message
                : "Couldn't reach the server. Try again.",
            );
          },
        );
      });

      root.replaceChildren(
        page(
          "Sign in",
          el("p", undefined, "Enter your email, and we'll send you a link to sign in."),
          form,
        ),
      );
      input.focus();
    }

    function showSent(address: string): void {
      root.replaceChildren(
        page(
          "Check your email",
          el(
            "p",
            undefined,
            `If ${address} belongs to a member, a sign-in link is on its way. It works once, within ${LINK_MINUTES} minutes.`,
          ),
          button("Use a different address", () => showForm(address), "link-button"),
        ),
      );
    }

    showForm(env.local.getItem(SIGN_IN_EMAIL_KEY) ?? "");
  };
}

/**
 * `/sign-in/confirm#<secret>`: the page a sign-in link opens. Opening it changes nothing (spec "Confirming before
 * signing in"): only the button spends the link, so a mail scanner that fetches the URL can't.
 */
export function confirmView(env: Env): View {
  return ({ root }) => {
    // The secret travels in the fragment, which is never sent to a server. Take it off the address bar too.
    const secret = env.location.hash.replace(/^#/, "");
    if (secret) {
      env.history.replaceState(null, "", "/sign-in/confirm");
    }

    function refused(message: string): void {
      root.replaceChildren(
        page(
          "Can't sign in",
          el("p", undefined, message),
          routeLink("/sign-in", "Send me a new link"),
        ),
      );
    }

    if (!secret) {
      refused("This sign-in link isn't complete. Ask for a new one.");
      return;
    }

    const email = env.local.getItem(SIGN_IN_EMAIL_KEY);
    const error = alertLine();
    const confirm = button(
      "Sign in",
      () => {
        confirm.disabled = true;
        error.clear();
        env.api.send<SessionMember>("POST", "/api/auth/confirm", { secret }).then(
          (member) => env.signedIn(member),
          (failure: unknown) => {
            if (failure instanceof ApiError && failure.status === 400) {
              refused(failure.message);
              return;
            }

            confirm.disabled = false;
            error.show("Couldn't reach the server. Try again.");
          },
        );
      },
      "primary",
    );

    root.replaceChildren(
      page(
        email ? `Sign in as ${email}?` : "Sign in to a PANOPTICON?",
        el("p", undefined, "This uses your sign-in link."),
        confirm,
        error.element,
      ),
    );
  };
}
