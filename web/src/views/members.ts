import { ApiError } from "../api";
import { alertLine, button, el, field, page, routeLink } from "../dom";
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
 * `/members` (spec team-members): everyone who shares a Project with you, and renaming yourself. Initial members see
 * every member of the server, may invite people to it (`canInviteMembers`, server-invites D4), and may remove anyone
 * else (`canRemoveMembers`). Everyone else invites people through a Project. Names are always set as text.
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
      } else if (env.member()?.canRemoveMembers) {
        item.append(
          button(
            "Remove",
            () => {
              if (
                !env.confirm(
                  `Remove ${member.displayName} (${member.email}) from the server? They leave every Project, their sessions end and their tokens stop working at once. Their past reports stay.`,
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

    function inviteForm(): HTMLElement {
      const { element: emailField, input: email } = field("Email", {
        type: "email",
        name: "email",
        required: true,
      });
      const invite = el("button", "primary", "Invite");
      invite.type = "submit";
      const error = alertLine();
      const form = el("form", "invite-form", emailField, invite, error.element);
      form.noValidate = true;
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const address = email.value.trim();
        if (!address) {
          error.show("Enter the email address to invite.");
          return;
        }

        error.clear();
        env.api.send<Member>("POST", "/api/members", { email: address }).then(
          async (added) => {
            email.value = "";
            status.textContent = `Invited ${added.email}. They'll get an email, and can sign in with that address now.`;
            await load();
          },
          (failure: unknown) => error.show(failureText(failure)),
        );
      });
      return el(
        "section",
        "members-invite",
        el("h3", undefined, "Invite someone to the server"),
        el(
          "p",
          undefined,
          "They get an email inviting them to sign in, and join no Project. To add them to one, invite them from its page.",
        ),
        form,
      );
    }

    const canInvite = env.member()?.canInviteMembers === true;
    root.replaceChildren(
      page(
        "Members",
        el(
          "p",
          undefined,
          canInvite ? "Everyone on this server." : "Everyone who shares a Project with you.",
        ),
        ...(canInvite ? [inviteForm()] : []),
        status,
        listError.element,
        list,
        ...(canInvite
          ? []
          : [
              el(
                "p",
                "members-invite-hint",
                "To invite someone, open one of your ",
                routeLink("/projects", "Projects"),
                ". Its owners can invite people from there.",
              ),
            ]),
      ),
    );
    await load();
  };
}
