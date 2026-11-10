import { ApiError } from "../api";
import { alertLine, button, el, field, page, routeLink, selectField } from "../dom";
import { codePlate } from "../projectLabel";
import type { View } from "../router";
import type { Env } from "../shell";

export type ProjectRole = "owner" | "editor" | "viewer";

/** A Project as its member sees it in the list (server: Projects/ProjectEndpoints ProjectView). */
export interface Project {
  id: string;
  name: string;
  code: string;
  /** The signed-in member's role. */
  role: ProjectRole;
}

/** A Project's member (server: ProjectMemberView). */
export interface ProjectMember {
  memberId: string;
  email: string;
  displayName: string;
  role: ProjectRole;
  /** ISO 8601. */
  joinedAt: string;
}

/** A Project's page (server: ProjectDetails): members come owners first. */
export interface ProjectDetails extends Project {
  members: ProjectMember[];
}

/** Names are 1 to 100 characters after trimming (server: ProjectNames). */
export const PROJECT_NAME_MAX = 100;

/** The server's rule (ProjectCodes.Rule), checked here too so a bad code needs no round trip. */
export const CODE_RULE = "A code is 2 to 10 letters or digits, starting with a letter.";
const CODE_PATTERN = /^[A-Za-z][A-Za-z0-9]{1,9}$/;

const ROLES: readonly (readonly [ProjectRole, string])[] = [
  ["owner", "Owner"],
  ["editor", "Editor"],
  ["viewer", "Viewer"],
];

const roleText = (role: ProjectRole) => ROLES.find(([value]) => value === role)?.[1] ?? role;

/** "an owner", "an editor", "a viewer". */
const aRole = (role: ProjectRole) => `${role === "viewer" ? "a" : "an"} ${role}`;

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/** The `.panopticon.json` that links a repo to the Project (cli-bridge, "Linking a repo to a Project"). */
export const linkFile = (projectId: string) =>
  `${JSON.stringify({ project: projectId }, null, 2)}\n`;

function nameIsValid(name: string): boolean {
  return name.length > 0 && name.length <= PROJECT_NAME_MAX;
}

const NAME_RULE = `A name is 1 to ${PROJECT_NAME_MAX} characters.`;

/** `/projects` (spec projects, "Projects list", "Creating a Project"): your Projects, and a form to make one. */
export function projectsView(env: Env): View {
  return async ({ root, navigate }) => {
    const list = el("ul", "project-list");
    const listError = alertLine();

    async function load(): Promise<void> {
      try {
        const projects = await env.api.get<Project[]>("/api/projects");
        list.replaceChildren(
          ...(projects.length > 0
            ? projects.map(row)
            : [
                el(
                  "li",
                  "project-empty",
                  el("p", undefined, "You're not in any Projects yet."),
                  el(
                    "p",
                    "project-empty-hint",
                    "Create one below, or ask a Project's owner to invite you.",
                  ),
                ),
              ]),
        );
        listError.clear();
      } catch (failure) {
        listError.show(failureText(failure));
      }
    }

    function row(project: Project): HTMLLIElement {
      return el(
        "li",
        "project",
        codePlate(project.code),
        routeLink(`/projects/${encodeURIComponent(project.id)}`, project.name, "project-name"),
        el("span", "project-role", roleText(project.role)),
      );
    }

    const { element: nameField, input: name } = field("Name", {
      name: "name",
      maxLength: PROJECT_NAME_MAX,
      required: true,
    });
    const { element: codeField, input: code } = field("Code", {
      name: "code",
      maxLength: 10,
      required: true,
      autocomplete: "off",
    });
    code.classList.add("code-input");
    const help = el("span", "field-help", "2 to 10 letters or digits, starting with a letter.");
    help.id = "project-code-help";
    code.setAttribute("aria-describedby", help.id);
    codeField.append(help);
    const create = el("button", "primary", "Create Project");
    create.type = "submit";
    const createError = alertLine();
    const form = el("form", "project-form", nameField, codeField, create, createError.element);
    form.noValidate = true;
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const projectName = name.value.trim();
      const projectCode = code.value.trim().toUpperCase();
      if (!nameIsValid(projectName)) {
        createError.show(NAME_RULE);
        return;
      }

      if (!CODE_PATTERN.test(projectCode)) {
        createError.show(CODE_RULE);
        return;
      }

      createError.clear();
      env.api.send<Project>("POST", "/api/projects", { name: projectName, code: projectCode }).then(
        (created) => navigate(`/projects/${encodeURIComponent(created.id)}`),
        (failure: unknown) => createError.show(failureText(failure)),
      );
    });

    root.replaceChildren(
      page(
        "Projects",
        listError.element,
        list,
        el("h3", undefined, "New Project"),
        el("p", undefined, "You'll be its owner, and can invite people to it."),
        form,
      ),
    );
    await load();
  };
}

/**
 * `/projects/{id}` (spec projects, "Project page", "Inviting to a Project", "Changing roles and removing"): the
 * Project, its link file and its members. Owners rename it and manage members; anyone can leave.
 */
export function projectView(env: Env): View {
  return async ({ root, route, navigate }) => {
    const projectId = route["id"] ?? "";
    const path = `/api/projects/${encodeURIComponent(projectId)}`;
    const back = routeLink("/projects", "All Projects", "back-link");
    const status = el("p", "form-status");
    status.setAttribute("role", "status");
    const loadError = alertLine();
    const body = el("div", "project-body");
    const plate = el("p", "project-plate");
    const heading = el("h2", undefined, "Project");

    async function load(): Promise<void> {
      let details: ProjectDetails;
      try {
        details = await env.api.get<ProjectDetails>(path);
      } catch (failure) {
        if (failure instanceof ApiError && failure.status === 404) {
          root.replaceChildren(
            page(
              "There's no such Project",
              back,
              el("p", undefined, "It may have been removed, or you're not one of its members."),
            ),
          );
          return;
        }

        loadError.show(failureText(failure));
        return;
      }

      loadError.clear();
      render(details);
    }

    function render(project: ProjectDetails): void {
      const isOwner = project.role === "owner";
      const me = env.member()?.id;
      heading.textContent = project.name;
      plate.replaceChildren(codePlate(project.code, "project-code-large"));
      const id = el("span", "project-id", project.id);
      const children: HTMLElement[] = [
        el("p", "project-meta", `You're ${aRole(project.role)}. Id `, id),
      ];
      if (isOwner) {
        children.push(renameForm(project));
      }

      children.push(
        linkBlock(project),
        el("h3", undefined, "Members"),
        memberList(project, isOwner, me),
      );
      if (isOwner) {
        children.push(inviteForm());
      }

      children.push(leave(project, me));
      body.replaceChildren(...children);
    }

    function renameForm(project: ProjectDetails): HTMLFormElement {
      const { element, input } = field("Name", {
        name: "name",
        value: project.name,
        maxLength: PROJECT_NAME_MAX,
        required: true,
      });
      const save = el("button", undefined, "Save");
      save.type = "submit";
      const error = alertLine();
      const form = el("form", "rename-form", element, save, error.element);
      form.noValidate = true;
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const name = input.value.trim();
        if (!nameIsValid(name)) {
          error.show(NAME_RULE);
          return;
        }

        env.api.send<ProjectDetails>("PATCH", path, { name }).then(
          (renamed) => {
            status.textContent = "The name is saved.";
            render(renamed);
          },
          (failure: unknown) => error.show(failureText(failure)),
        );
      });
      return form;
    }

    function linkBlock(project: ProjectDetails): HTMLElement {
      const text = linkFile(project.id);
      const block = el("pre", "link-file", text);
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
      return el(
        "section",
        "link-repo",
        el("h3", undefined, "Link a repo"),
        el(
          "p",
          undefined,
          `Commit this as .panopticon.json at the root of a repo, and its reports go to ${project.code}.`,
        ),
        block,
        el("div", "copy-row", copy, copied),
      );
    }

    function memberList(
      project: ProjectDetails,
      isOwner: boolean,
      me: string | undefined,
    ): HTMLElement {
      const error = alertLine();
      const list = el("ul", "member-list project-members");
      for (const member of project.members) {
        const name = el("span", "member-name", member.displayName);
        if (member.memberId === me) {
          name.append(el("span", "member-you", " (you)"));
        }

        const item = el(
          "li",
          "member",
          name,
          el("span", "member-meta", el("span", "member-email", member.email)),
        );
        if (isOwner) {
          const { element, select } = selectField(
            `Role for ${member.displayName}`,
            "role",
            ROLES,
            member.role,
          );
          element.classList.add("role-field");
          element.querySelector(".field-label")?.classList.add("visually-hidden");
          select.addEventListener("change", () => {
            const role = select.value as ProjectRole;
            // Stepping down yourself can't be undone by you, so it asks first (Emmz, 2026-11-10).
            if (
              member.memberId === me &&
              role !== "owner" &&
              !env.confirm(
                `Make yourself ${aRole(role)} of ${project.code}? You'll lose owner controls, and only another owner can give them back.`,
              )
            ) {
              select.value = member.role;
              return;
            }

            error.clear();
            env.api
              .send<ProjectMember>(
                "PATCH",
                `${path}/members/${encodeURIComponent(member.memberId)}`,
                { role },
              )
              .then(
                async () => {
                  status.textContent = `${member.displayName} is now ${aRole(role)}.`;
                  await load();
                },
                (failure: unknown) => {
                  select.value = member.role;
                  error.show(failureText(failure));
                },
              );
          });
          item.append(element);
          if (member.memberId !== me) {
            item.append(
              button(
                "Remove",
                () => {
                  if (
                    !env.confirm(
                      `Remove ${member.displayName} (${member.email}) from ${project.code}? They stop seeing its work at once.`,
                    )
                  ) {
                    return;
                  }

                  env.api
                    .send("DELETE", `${path}/members/${encodeURIComponent(member.memberId)}`)
                    .then(
                      async () => {
                        status.textContent = `Removed ${member.email} from ${project.code}.`;
                        await load();
                      },
                      (failure: unknown) => error.show(failureText(failure)),
                    );
                },
                "danger-button",
              ),
            );
          }
        } else {
          item.append(el("span", "project-role", roleText(member.role)));
        }

        list.append(item);
      }

      return el("div", "project-members-block", error.element, list);
    }

    function inviteForm(): HTMLElement {
      const { element: emailField, input: email } = field("Email", {
        type: "email",
        name: "email",
        required: true,
      });
      const { element: roleField, select: role } = selectField("Role", "role", ROLES, "viewer");
      const invite = el("button", "primary", "Invite");
      invite.type = "submit";
      const error = alertLine();
      const form = el("form", "invite-form", emailField, roleField, invite, error.element);
      form.noValidate = true;
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const address = email.value.trim();
        if (!address) {
          error.show("Enter the email address to invite.");
          return;
        }

        error.clear();
        env.api
          .send<ProjectMember>("POST", `${path}/members`, { email: address, role: role.value })
          .then(
            async (added) => {
              status.textContent = `Invited ${added.email} as ${aRole(added.role)}. They can sign in with that address now.`;
              await load();
            },
            (failure: unknown) => error.show(failureText(failure)),
          );
      });
      return el(
        "section",
        "project-invite",
        el("h3", undefined, "Invite someone"),
        el(
          "p",
          undefined,
          "If they aren't on this server yet, this lets them sign in too. No invitation email is sent.",
        ),
        form,
      );
    }

    function leave(project: ProjectDetails, me: string | undefined): HTMLElement {
      const error = alertLine();
      const leaveButton = button(
        "Leave this Project",
        () => {
          if (
            !me ||
            !env.confirm(
              `Leave ${project.code}? You stop seeing its work, and your reports into it are refused.`,
            )
          ) {
            return;
          }

          env.api.send("DELETE", `${path}/members/${encodeURIComponent(me)}`).then(
            () => navigate("/projects"),
            (failure: unknown) => error.show(failureText(failure)),
          );
        },
        "leave-button",
      );
      return el("div", "actions project-leave", leaveButton, error.element);
    }

    // The back link, then the plate (the loudest thing on the page), then the name.
    root.replaceChildren(
      el("section", "page project-page", back, plate, heading, status, loadError.element, body),
    );
    await load();
  };
}
