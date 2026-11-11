import { ApiError, type Api } from "../api";
import { alertLine, button, el, field, page, routeLink, selectField } from "../dom";
import { PROJECT_TAGS_CHANGED, parseProjectTagsChange, type EventSourceLike } from "../live";
import { codePlate } from "../projectLabel";
import { tagChip, tagText } from "../tagChips";
import type { View } from "../router";
import type { Env } from "../shell";
import { setupGuide, stepsDone } from "./setupGuide";
import type { Token } from "./tokens";

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

/** One of a Project's tags on its page (server: ProjectTagView), with how many changes carry it. */
export interface ProjectTag {
  id: string;
  key: string;
  value: string | null;
  /** Its canonical text, e.g. "area: onboarding". */
  text: string;
  changeCount: number;
}

/** A Project's page (server: ProjectDetails): members come owners first, and tags by key then value. */
export interface ProjectDetails extends Project {
  members: ProjectMember[];
  tags: ProjectTag[];
}

/** The most tags a Project may have (server: TagText.MaxProjectTags). */
export const PROJECT_TAG_MAX = 200;

/** "No changes", "1 change", "12 changes". */
const changeCount = (count: number) =>
  count === 0 ? "No changes" : count === 1 ? "1 change" : `${count} changes`;

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

/** Set when a member hides the setup guide on the Projects page; per browser (onboarding-guide D4). */
export const SETUP_GUIDE_HIDDEN_KEY = "panopticon.setupGuide.hidden";

/** `/projects` (spec projects, "Projects list", "Creating a Project"): your Projects, and a form to make one. */
export function projectsView(env: Env): View {
  return async ({ root, navigate }) => {
    const list = el("ul", "project-list");
    const listError = alertLine();
    const guideSlot = el("div", "projects-setup");

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

    // Spec machine-setup, "Guide on the Projects page": until one of your tokens has reported, unless hidden here.
    async function loadGuide(): Promise<void> {
      if (env.local.getItem(SETUP_GUIDE_HIDDEN_KEY) === "1") {
        return;
      }

      let tokens: Token[];
      try {
        tokens = await env.api.get<Token[]>("/api/tokens");
      } catch {
        // Without the tokens there's no telling whether this member is set up; the Projects list is the page's job.
        return;
      }

      if (stepsDone(tokens).connect) {
        return;
      }

      const guide = setupGuide(env, {
        onHide: () => {
          env.local.setItem(SETUP_GUIDE_HIDDEN_KEY, "1");
          guideSlot.replaceChildren();
        },
      });
      guide.setTokens(tokens);
      guideSlot.replaceChildren(guide.element);
      await guide.load();
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
        guideSlot,
        listError.element,
        list,
        el("h3", undefined, "New Project"),
        el("p", undefined, "You'll be its owner, and can invite people to it."),
        form,
      ),
    );
    await Promise.all([load(), loadGuide()]);
  };
}

/** What the Project page's Tags section needs from the page around it. */
export interface TagSectionDeps {
  api: Api;
  /** The Project's API path, `/api/projects/{id}`. */
  path: string;
  confirm: (message: string) => boolean;
  /** The page's status line, for "Added …", "Renamed …", "Deleted …" and "… was deleted." */
  status: HTMLElement;
  /** Reloads the whole page, after the section's own writes. */
  reload: () => Promise<void>;
}

/** The Project page's Tags section (web/DESIGN.md "Tags"). */
export interface TagSection {
  element: HTMLElement;
  /**
   * Shows `project`'s tags in place (project-page-live-tags D2): the rest of the section stays, and so does a row
   * being renamed whose tag is still there. The role is the one the section was built with.
   */
  redraw(project: ProjectDetails): void;
}

interface TagRow {
  item: HTMLLIElement;
  /** True while the row shows its rename form. */
  renaming(): boolean;
  /** Gives the row the tag as it is now; a row being renamed keeps its form. */
  update(tag: ProjectTag): void;
  /** Its tag with the code, e.g. "PAN/bug". */
  text(): string;
}

/**
 * Puts `children` in `parent` in order, moving as few nodes as it can, so a row being edited usually isn't moved at
 * all. Moving a node blurs whatever has focus inside it, so focus (and an input's selection) is put back after.
 */
function arrange(parent: HTMLElement, children: readonly HTMLElement[]): void {
  const active = document.activeElement;
  const focused = active instanceof HTMLElement && parent.contains(active) ? active : null;
  const selection =
    focused instanceof HTMLInputElement
      ? { start: focused.selectionStart, end: focused.selectionEnd }
      : null;

  let cursor = parent.firstChild;
  for (const child of children) {
    if (child === cursor) {
      cursor = cursor.nextSibling;
    } else {
      parent.insertBefore(child, cursor);
    }
  }

  while (cursor) {
    const next = cursor.nextSibling;
    cursor.remove();
    cursor = next;
  }

  if (focused?.isConnected && document.activeElement !== focused) {
    focused.focus();
    if (selection && focused instanceof HTMLInputElement) {
      focused.setSelectionRange(selection.start, selection.end);
    }
  }
}

// web/DESIGN.md "Tags": everyone sees the list and counts; owners and editors add, rename and delete.
export function projectTagSection(deps: TagSectionDeps, initial: ProjectDetails): TagSection {
  const { code } = initial;
  const canManage = initial.role === "owner" || initial.role === "editor";
  const section = el("section", "project-tags");
  const heading = el("h3", undefined, "Tags");
  const empty = el(
    "p",
    "project-tags-empty",
    canManage
      ? `${code} has no tags yet. Add one here, or from a change's page.`
      : `${code} has no tags yet.`,
  );
  const error = alertLine();
  const list = el("ul", "member-list project-tag-list");
  list.setAttribute("aria-label", `${code}'s tags`);
  const hint = el("p", "project-tags-hint", "Owners and editors manage these tags.");
  const add = canManage ? addTagForm() : undefined;
  let rows = new Map<string, TagRow>();

  function redraw(project: ProjectDetails): void {
    const next = new Map<string, TagRow>();
    for (const tag of project.tags) {
      const id = tag.id.toLowerCase();
      const kept = rows.get(id);
      if (kept?.renaming()) {
        kept.update(tag);
        next.set(id, kept);
      } else {
        next.set(id, tagRow(tag));
      }
    }

    // D2: a tag deleted while its rename form was open goes, and the page says so.
    for (const [id, row] of rows) {
      if (row.renaming() && !next.has(id)) {
        deps.status.textContent = `${row.text()} was deleted.`;
      }
    }

    rows = next;
    arrange(
      list,
      [...next.values()].map((row) => row.item),
    );
    const parts: HTMLElement[] = [heading];
    if (project.tags.length === 0) {
      error.clear();
      parts.push(empty);
    } else {
      parts.push(list, error.element);
      if (!canManage) {
        parts.push(hint);
      }
    }

    if (add) {
      add.update(project.tags.length);
      parts.push(add.form);
    }

    arrange(section, parts);
  }

  function tagRow(initialTag: ProjectTag): TagRow {
    let tag = initialTag;
    let renaming = false;
    const name = () => tagText(tag, code);
    const item = el("li", "member project-tag");
    const show = () => {
      renaming = false;
      item.replaceChildren(
        tagChip(tag, code, false),
        el("span", "project-tag-count", changeCount(tag.changeCount)),
      );
      if (canManage) {
        item.append(
          el(
            "span",
            "project-tag-actions",
            button("Rename", rename, "danger-button"),
            button("Delete", remove, "danger-button"),
          ),
        );
      }
    };

    function rename(): void {
      const before = name();
      const { element, input } = field(`Rename ${before}`, { name: "tag", value: tag.text });
      input.autocomplete = "off";
      const save = el("button", "primary", "Save");
      save.type = "submit";
      const cancel = button("Cancel", () => {
        show();
        item.querySelector("button")?.focus();
      });
      const renameError = alertLine();
      const form = el(
        "form",
        "rename-form tag-rename-form",
        element,
        save,
        cancel,
        renameError.element,
      );
      form.noValidate = true;
      form.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          cancel.click();
        }
      });
      form.addEventListener("submit", (event) => {
        event.preventDefault();
        const text = input.value.trim();
        if (!text) {
          renameError.show("Enter the tag's new text.");
          return;
        }

        renameError.clear();
        const from = name();
        deps.api
          .send<ProjectTag>("PATCH", `${deps.path}/tags/${encodeURIComponent(tag.id)}`, {
            tag: text,
          })
          .then(
            async (renamed) => {
              deps.status.textContent = `Renamed ${from} to ${tagText(renamed, code)}.`;
              await deps.reload();
            },
            (failure: unknown) => renameError.show(failureText(failure)),
          );
      });
      renaming = true;
      item.replaceChildren(form);
      input.focus();
      input.select();
    }

    function remove(): void {
      const current = name();
      const question =
        tag.changeCount === 0
          ? `Delete ${current}? No changes carry it.`
          : `${current} is on ${changeCount(tag.changeCount).toLowerCase()}. Delete it?`;
      if (!deps.confirm(question)) {
        return;
      }

      error.clear();
      deps.api.send("DELETE", `${deps.path}/tags/${encodeURIComponent(tag.id)}`).then(
        async () => {
          deps.status.textContent = `Deleted ${current}.`;
          await deps.reload();
        },
        (failure: unknown) => error.show(failureText(failure)),
      );
    }

    function update(next: ProjectTag): void {
      tag = next;
      if (!renaming) {
        show();
      }
    }

    update(tag);
    return { item, renaming: () => renaming, update, text: name };
  }

  function addTagForm(): { form: HTMLFormElement; update(count: number): void } {
    const { element, input } = field("Tag", { name: "tag", autocomplete: "off" });
    const help = el("span", "field-help");
    help.id = "project-tag-help";
    input.setAttribute("aria-describedby", help.id);
    element.append(help);
    const submit = el("button", "primary", "Add tag");
    submit.type = "submit";
    const formError = alertLine();
    const form = el("form", "project-form tag-add-form", element, submit, formError.element);
    form.noValidate = true;
    form.addEventListener("submit", (event) => {
      event.preventDefault();
      const text = input.value.trim();
      if (!text) {
        formError.show("Enter a tag to add.");
        return;
      }

      formError.clear();
      deps.api.send<ProjectTag>("POST", `${deps.path}/tags`, { tag: text }).then(
        async (added) => {
          deps.status.textContent = `Added ${tagText(added, code)}.`;
          await deps.reload();
        },
        (failure: unknown) => formError.show(failureText(failure)),
      );
    });
    return {
      form,
      update(count) {
        help.textContent =
          count >= PROJECT_TAG_MAX
            ? `${code} has ${PROJECT_TAG_MAX} tags, the most it can have.`
            : "A key, or a key and a value: bug, or area: onboarding.";
      },
    };
  }

  redraw(initial);
  return { element: section, redraw };
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
    // The Tags section as last rendered: a live update redraws just its rows (project-page-live-tags D2).
    let tags: TagSection | undefined;
    let stream: EventSourceLike | undefined;
    let left = false;
    // D3: every fetch of the page takes a ticket, and a live refresh applies only if no newer one has started.
    let ticket = 0;

    /** Not a member (any more): the page says so, as a reload would, and stops following the stream. */
    function notFound(): void {
      stopFollowing();
      root.replaceChildren(
        page(
          "There's no such Project",
          back,
          el("p", undefined, "It may have been removed, or you're not one of its members."),
        ),
      );
    }

    /** Loads and renders the whole page; true when it rendered. */
    async function load(): Promise<boolean> {
      ++ticket;
      let details: ProjectDetails;
      try {
        details = await env.api.get<ProjectDetails>(path);
      } catch (failure) {
        if (left) {
          return false;
        }

        if (failure instanceof ApiError && failure.status === 404) {
          notFound();
          return false;
        }

        loadError.show(failureText(failure));
        return false;
      }

      if (left) {
        return false;
      }

      loadError.clear();
      render(details);
      return true;
    }

    // D1: the event has no counts, so the Project is fetched again, and only its tags are used.
    function refreshTags(): void {
      const mine = ++ticket;
      env.api.get<ProjectDetails>(path).then(
        (details) => {
          if (mine === ticket && !left) {
            tags?.redraw(details);
          }
        },
        (failure: unknown) => {
          if (mine !== ticket || left) {
            return;
          }

          if (failure instanceof ApiError && failure.status === 404) {
            notFound();
          }

          // Otherwise the rows stay as they are; the next event or reconnect tries again.
        },
      );
    }

    // D4: opened once the first load has rendered, and closed when the page is left.
    function follow(): void {
      const create = env.createEventSource ?? ((url: string) => new EventSource(url));
      const source = create("/api/events");
      stream = source;
      let opened = false;
      // A reconnect may have missed events, so it catches up; the first open needs nothing.
      source.onopen = () => {
        if (opened) {
          refreshTags();
        }

        opened = true;
      };
      source.onerror = () => {
        // The browser retries by itself; the page keeps its rows meanwhile.
      };
      source.addEventListener(PROJECT_TAGS_CHANGED, (event) => {
        const change = parseProjectTagsChange(event.data);
        if (change?.projectId.toLowerCase() === projectId.toLowerCase()) {
          refreshTags();
        }
      });
    }

    function stopFollowing(): void {
      stream?.close();
      stream = undefined;
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

      tags = projectTagSection(
        {
          api: env.api,
          path,
          confirm: env.confirm,
          status,
          reload: async () => {
            await load();
          },
        },
        project,
      );
      children.push(
        linkBlock(project),
        tags.element,
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
        el(
          "p",
          "link-repo-agent",
          `Or ask your agent to link this repo to ${project.code}: it can look the Project up and write the file.`,
        ),
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
              .send<ProjectDetails>(
                "PATCH",
                `${path}/members/${encodeURIComponent(member.memberId)}`,
                { role },
              )
              .then(
                (changed) => {
                  // The answer is the Project's page as it is now, so it renders with no second fetch (review fix 12.6).
                  render(changed);
                  status.textContent = `${member.displayName} is now ${aRole(role)}.`;
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
    if (await load()) {
      follow();
    }

    return () => {
      left = true;
      stopFollowing();
    };
  };
}
