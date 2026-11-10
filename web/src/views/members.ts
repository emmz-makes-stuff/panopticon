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
 * `/members` (spec team-members): everyone who shares a Project with you, renaming yourself, and (for initial members,
 * `canRemoveMembers`) removing anyone else from the server. People join through a Project, so there is no invite here.
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

    root.replaceChildren(
      page(
        "Members",
        el("p", undefined, "Everyone who shares a Project with you."),
        status,
        listError.element,
        list,
        el(
          "p",
          "members-invite-hint",
          "To invite someone, open one of your ",
          routeLink("/projects", "Projects"),
          ". Its owners can invite people from there.",
        ),
      ),
    );
    await load();
  };
}
