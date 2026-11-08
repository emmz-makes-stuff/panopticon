import { ApiError } from "../api";
import { alertLine, button, el, field, page } from "../dom";
import { calendarDate } from "../format";
import type { View } from "../router";
import type { Env } from "../shell";

/** An active member (server: Auth/MemberEndpoints MemberView). */
export interface Member {
  id: string;
  email: string;
  displayName: string;
  /** ISO 8601. */
  joinedAt: string;
}

/** Display names are 1 to 50 characters after trimming (server: MemberNames.MaxLength). */
export const DISPLAY_NAME_MAX = 50;

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/**
 * `/members` (spec team-members): every active member, inviting someone, renaming yourself, and removing anyone else.
 * Names are always set as text.
 */
export function membersView(env: Env): View {
  return async ({ root }) => {
    const list = el("ul", "member-list");
    const listError = alertLine();
    const status = el("p", "form-status");
    status.setAttribute("role", "status");

    async function load(): Promise<void> {
      try {
        const members = await env.api.get<Member[]>("/api/members");
        list.replaceChildren(...members.map(row));
        listError.clear();
      } catch (failure) {
        listError.show(failureText(failure));
      }
    }

    function row(member: Member): HTMLLIElement {
      const isMe = member.id === env.member()?.id;
      const name = el("span", "member-name", member.displayName);
      const meta = el(
        "span",
        "member-meta",
        el("span", "member-email", member.email),
        el("span", "member-joined", `Joined ${calendarDate(member.joinedAt)}`),
      );
      const item = el("li", "member", name, meta);
      if (isMe) {
        name.append(el("span", "member-you", " (you)"));
        item.append(renameForm(member));
      } else {
        item.append(
          button(
            "Remove",
            () => {
              if (
                !env.confirm(
                  `Remove ${member.displayName} (${member.email})? Their sessions end and their tokens stop working at once. Their past reports stay.`,
                )
              ) {
                return;
              }

              env.api.send("DELETE", `/api/members/${encodeURIComponent(member.id)}`).then(
                async () => {
                  status.textContent = `Removed ${member.email}.`;
                  await load();
                },
                (failure: unknown) => listError.show(failureText(failure)),
              );
            },
            "danger-button",
          ),
        );
      }

      return item;
    }

    function renameForm(member: Member): HTMLFormElement {
      const { element, input } = field("Your display name", {
        name: "displayName",
        value: member.displayName,
        maxLength: DISPLAY_NAME_MAX,
        required: true,
      });
      const save = el("button", undefined, "Save");
      save.type = "submit";
      const error = alertLine();
      const form = el("form", "rename-form", element, save, error.element);
      form.noValidate = true;
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const displayName = input.value.trim();
        if (!displayName || displayName.length > DISPLAY_NAME_MAX) {
          error.show(`A display name must be 1 to ${DISPLAY_NAME_MAX} characters.`);
          return;
        }

        env.api.send<Member>("PATCH", "/api/members/me", { displayName }).then(
          async (renamed) => {
            env.renamed(renamed.displayName);
            status.textContent = "Your display name is saved.";
            await load();
          },
          (failure: unknown) => error.show(failureText(failure)),
        );
      });
      return form;
    }

    const { element: emailField, input: email } = field("Email", {
      type: "email",
      name: "email",
      required: true,
    });
    const { element: nameField, input: displayName } = field("Display name (optional)", {
      name: "displayName",
      maxLength: DISPLAY_NAME_MAX,
    });
    const invite = el("button", "primary", "Invite");
    invite.type = "submit";
    const inviteError = alertLine();
    const inviteForm = el(
      "form",
      "invite-form",
      emailField,
      nameField,
      invite,
      inviteError.element,
    );
    inviteForm.noValidate = true;
    inviteForm.addEventListener("submit", (event) => {
      event.preventDefault();
      const address = email.value.trim();
      if (!address) {
        inviteError.show("Enter the email address to invite.");
        return;
      }

      inviteError.clear();
      const name = displayName.value.trim();
      env.api
        .send<Member>(
          "POST",
          "/api/members",
          name ? { email: address, displayName: name } : { email: address },
        )
        .then(
          async (added) => {
            email.value = "";
            displayName.value = "";
            status.textContent = `Invited ${added.email}. They can sign in with that address now.`;
            await load();
          },
          (failure: unknown) => inviteError.show(failureText(failure)),
        );
    });

    root.replaceChildren(
      page(
        "Members",
        status,
        listError.element,
        list,
        el("h3", undefined, "Invite someone"),
        el(
          "p",
          undefined,
          "They become a member at once, and sign in with their email. No invitation email is sent.",
        ),
        inviteForm,
      ),
    );
    await load();
  };
}
