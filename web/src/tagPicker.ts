import { ApiError } from "./api";
import { alertLine, button, el } from "./dom";
import type { Env } from "./shell";
import { ordinal } from "./filters";
import { retagCard, type ProjectTagsChange } from "./live";
import { tagChips, tagText } from "./tagChips";
import { parseTag, sameTag } from "./tagSyntax";
import type { ChangeCard } from "./types";
import type { ProjectDetails, ProjectTag } from "./views/projects";

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/**
 * The change page's tags (tags D4, D9; web/DESIGN.md "Tags"): the chips, and for the Project's owners and editors an
 * "Edit tags" panel that toggles the Project's tags on the change and can add a new one. One element for the page's
 * life, so the header can be re-rendered by a live refresh without closing an open panel. `onCard` gets the card each
 * tag change returns, so the page's own copy stays current (a header re-render then shows the same tags).
 */
export function tagPicker(
  env: Env,
  changeId: string,
  onCard: (card: ChangeCard) => void = () => undefined,
) {
  const element = el("div", "change-tags");
  const chipsSlot = el("div", "change-tags-chips");
  const status = el("p", "form-status tag-picker-status");
  status.setAttribute("role", "status");
  let card: ChangeCard | undefined;
  let project: ProjectDetails | null = null;
  let loadedFor: string | null = null;
  let open = false;

  const toggle = button(
    "Edit tags",
    () => (open ? close() : void openPanel()),
    "danger-button tag-picker-toggle",
  );
  toggle.setAttribute("aria-expanded", "false");
  const panel = el("div", "tag-picker");
  panel.id = `tag-picker-${changeId}`;
  panel.hidden = true;
  toggle.setAttribute("aria-controls", panel.id);
  const search = document.createElement("input");
  search.type = "search";
  search.autocomplete = "off";
  search.id = `tag-picker-search-${changeId}`;
  const list = el("div", "tag-picker-list");
  const error = alertLine();
  const legend = el("legend", "tag-picker-heading");
  const fieldset = el(
    "fieldset",
    "tag-picker-fieldset",
    legend,
    el("label", "field tag-picker-search", el("span", "field-label", "Find or add a tag"), search),
    list,
  );
  const personalNote = el(
    "p",
    "tag-picker-personal",
    "Your personal tags come from `panopticon tags` on your machines.",
  );
  panel.append(fieldset, error.element, personalNote);
  panel.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      close();
    }
  });
  search.addEventListener("input", () => renderList());

  /** Shows `next`; the first time for a Project, learns whether the viewer may edit its tags. */
  function update(next: ChangeCard): void {
    card = next;
    renderChips();
    const projectId = next.project?.id ?? null;
    if (projectId !== loadedFor) {
      loadedFor = projectId;
      project = null;
      if (projectId) {
        void loadProject(projectId);
      }
    }
  }

  async function loadProject(projectId: string): Promise<void> {
    try {
      const details = await env.api.get<ProjectDetails>(
        `/api/projects/${encodeURIComponent(projectId)}`,
      );
      if (loadedFor !== projectId) {
        return;
      }

      // Re-render only what changed, so a refetch on opening doesn't replace the checkbox that has focus.
      const tagsChanged = JSON.stringify(project?.tags) !== JSON.stringify(details.tags);
      const roleChanged = project?.role !== details.role;
      project = details;
      if (roleChanged) {
        renderChips();
      }

      if (open && tagsChanged) {
        renderList();
      }
    } catch {
      // No Project page for this viewer: they see the chips only.
    }
  }

  const canEdit = () => project !== null && (project.role === "owner" || project.role === "editor");

  function renderChips(): void {
    const chips = card ? tagChips(card) : null;
    chipsSlot.replaceChildren(...(chips ? [chips] : []));
    const editing = canEdit();
    if (!editing && open) {
      close();
    }

    const parts: HTMLElement[] = [];
    if (chips || editing) {
      parts.push(el("div", "change-tags-row", chipsSlot, ...(editing ? [toggle] : [])));
    }

    if (editing) {
      parts.push(panel, status);
    }

    element.replaceChildren(...parts);
  }

  async function openPanel(): Promise<void> {
    open = true;
    panel.hidden = false;
    toggle.textContent = "Done";
    toggle.setAttribute("aria-expanded", "true");
    if (project) {
      legend.textContent = `${project.code}'s tags`;
    }

    renderList();
    search.focus();
    if (loadedFor) {
      await loadProject(loadedFor); // Fresh, so a tag added elsewhere is offered.
    }
  }

  function close(): void {
    open = false;
    panel.hidden = true;
    toggle.textContent = "Edit tags";
    toggle.setAttribute("aria-expanded", "false");
    search.value = "";
    error.clear();
    if (element.contains(toggle)) {
      toggle.focus();
    }
  }

  function renderList(): void {
    if (!project || !card) {
      list.replaceChildren();
      return;
    }

    const code = project.code;
    legend.textContent = `${code}'s tags`;
    const on = new Set(card.projectTags.map((t) => t.id));
    const typed = search.value.trim();
    const needle = typed.toLowerCase();
    const shown = project.tags.filter(
      (t) => needle === "" || t.text.toLowerCase().includes(needle),
    );
    const rows: HTMLElement[] = [];
    if (project.tags.length === 0) {
      rows.push(el("p", "tag-picker-empty", `${code} has no tags yet. Type one to add it.`));
    }

    for (const tag of shown) {
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = on.has(tag.id);
      box.addEventListener("change", () => void setTag(tag, box.checked, box));
      rows.push(el("label", "tag-picker-option", box, el("span", undefined, tag.text)));
    }

    error.clear();
    if (typed) {
      const parsed = parseTag(typed);
      if ("tag" in parsed) {
        const wanted = parsed.tag;
        if (!project.tags.some((t) => sameTag(t, wanted))) {
          const text = tagText(wanted, code);
          rows.push(button(`+ Add ${text}`, () => void addNew(typed), "tag-picker-add"));
        }
      } else if (shown.length === 0) {
        error.show(parsed.error);
      }
    }

    list.replaceChildren(...rows);
  }

  async function send(body: { add?: string[]; remove?: string[] }): Promise<ChangeCard> {
    const next = await env.api.send<ChangeCard>(
      "POST",
      `/api/changes/${encodeURIComponent(changeId)}/tags`,
      body,
    );
    onCard(next);
    return next;
  }

  async function setTag(tag: ProjectTag, on: boolean, box: HTMLInputElement): Promise<void> {
    if (!project) {
      return;
    }

    const text = tagText(tag, project.code);
    error.clear();
    try {
      card = await send(on ? { add: [tag.id] } : { remove: [tag.id] });
      status.textContent = `${on ? "Added" : "Removed"} ${text}.`;
      renderChips();
    } catch (failure) {
      box.checked = !on; // Back to what the server has.
      error.show(failureText(failure));
    }
  }

  async function addNew(typed: string): Promise<void> {
    if (!project) {
      return;
    }

    const projectPath = `/api/projects/${encodeURIComponent(project.id)}`;
    error.clear();
    let added: ProjectTag;
    try {
      added = await env.api.send<ProjectTag>("POST", `${projectPath}/tags`, { tag: typed });
    } catch (failure) {
      error.show(failureText(failure));
      return;
    }

    project = { ...project, tags: [...project.tags, added].sort(byTagOrder) };
    const text = tagText(added, project.code);
    search.value = "";
    try {
      card = await send({ add: [added.id] });
      status.textContent = `Added ${text} to ${project.code}'s tags and to this change.`;
      renderChips();
    } catch (failure) {
      error.show(
        `Added ${text} to ${project.code}'s tags, but couldn't put it on this change: ${failureText(failure)}`,
      );
    }

    renderList();
    search.focus();
  }

  /**
   * The Project's tags changed (tags D6): the chips are renamed and dropped by id on the card this picker shows (which
   * a toggle may have made newer than the page's), and the Project's list is loaded again for an open panel.
   */
  function projectTagsChanged(change: ProjectTagsChange): void {
    if (card) {
      const next = retagCard(card, change);
      if (next !== card) {
        card = next;
        renderChips();
        if (open) {
          renderList();
        }
      }
    }

    if (loadedFor) {
      void loadProject(loadedFor);
    }
  }

  return { element, update, projectTagsChanged };
}

/** The server's order for a Project's tags (ProjectTagOrder): by key, then value (a bare key first), ordinally. */
function byTagOrder(a: ProjectTag, b: ProjectTag): number {
  return (
    ordinal(a.key.toLowerCase(), b.key.toLowerCase()) ||
    ordinal((a.value ?? "").toLowerCase(), (b.value ?? "").toLowerCase())
  );
}
