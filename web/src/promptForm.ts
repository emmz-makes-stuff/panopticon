import { ApiError, type Api } from "./api";
import type { PromptDetail, PromptKind, PromptQuestion, SessionView, VisitRequest } from "./types";

/**
 * The form that answers a held prompt from the dashboard (spec remote-answers, Question forms, Permission prompts and
 * Safe rendering; agent-interaction D3). It posts `/api/prompts/{id}/answer` through the app's `Api`, and says what
 * went wrong inline, in text.
 *
 * Previews of options arrive as HTML the server has already made safe (`DocumentRenderer`, as documents are), and go
 * in as is. A permission prompt's input is a command, not Markdown: it goes in a `<pre>` as text.
 */

/** The longest Other answer, deny message and visit note the server takes (`PromptAnswerEndpoints`). */
export const MAX_ANSWER_LENGTH = 2000;
export const MAX_MESSAGE_LENGTH = 500;
export const MAX_VISIT_NOTE_LENGTH = 1000;

/** `POST /api/prompts/{id}/answer`'s body. */
export type PromptAnswerBody =
  | { answers: Record<string, string> }
  | { allow: boolean; message?: string }
  | { visited: boolean; message?: string };

/** The form for `detail`, a held prompt of `session`'s: a question's, a permission prompt's, or a visit request's. */
export function renderPromptForm(
  session: SessionView,
  detail: PromptDetail,
  api: Api,
): HTMLFormElement {
  if (detail.visit) {
    return visitForm(detail, detail.visit, api);
  }

  return detail.questions?.length
    ? questionForm(detail, detail.questions, api)
    : permissionForm(session, detail, api);
}

/**
 * The link for a visit request's URL, or null when it isn't an `http:` or `https:` URL (visit-requests D7). The
 * server refuses anything else, but the page checks again before it makes a link.
 */
export function visitHref(url: string): string | null {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch {
    return null;
  }
}

/**
 * A question's answer as the session gets it (spec remote-answers, Question forms): the chosen labels, joined by
 * ", ", with any Other text as one more item. A single-choice question takes the Other text in place of a label. An
 * empty string means it isn't answered yet.
 */
export function joinAnswer(chosen: readonly string[], other: string, multiSelect: boolean): string {
  const text = other.trim();
  if (!multiSelect) {
    return text || chosen[0] || "";
  }

  return [...chosen, ...(text ? [text] : [])].join(", ");
}

/** Whether a question is a yes-or-no one, shown as two buttons: single choice, with exactly the options Yes and No. */
export function isYesNo(question: PromptQuestion): boolean {
  if (question.multiSelect || question.options.length !== 2) {
    return false;
  }

  const labels = question.options.map((o) => o.label.trim().toLowerCase()).sort();
  return labels[0] === "no" && labels[1] === "yes";
}

/** What the form says when the answer was refused, by the status the server gave. */
export function answerError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.status) {
      case 403:
        return "Only the session's member and its Project's owners can answer this.";
      case 404:
        return "This prompt has gone.";
      case 409:
        return "This prompt has already been answered, or has gone back to the terminal.";
      default:
        return error.detail ?? error.message;
    }
  }

  return "The answer didn't reach the server. Try again.";
}

function questionForm(
  detail: PromptDetail,
  questions: readonly PromptQuestion[],
  api: Api,
): HTMLFormElement {
  const form = baseForm(detail, "question");
  const readers = questions.map((question, index) => {
    const { fieldset, read } = questionFieldset(question, `${detail.id}-${index}`);
    form.append(fieldset);
    return { question, read };
  });

  const send = button("Send answer", "submit", "prompt-send");
  const { error, status } = messages(form);
  form.append(send);

  form.addEventListener("submit", (event) => {
    event.preventDefault();
    const answers: Record<string, string> = {};
    for (const { question, read } of readers) {
      const answer = read();
      if (!answer) {
        showError(error, `Answer "${question.header || question.question}" first.`);
        return;
      }

      answers[question.question] = answer;
    }

    void post(form, api, detail.id, { answers }, error, status);
  });

  return form;
}

function questionFieldset(
  question: PromptQuestion,
  name: string,
): { fieldset: HTMLFieldSetElement; read: () => string } {
  const fieldset = element("fieldset", "prompt-question");
  const legend = element("legend", "prompt-legend");
  if (question.header) {
    legend.append(element("span", "prompt-header", question.header), " ");
  }

  legend.append(element("span", "prompt-text", question.question));
  fieldset.append(legend);

  const other = document.createElement("input");
  other.type = "text";
  other.className = "prompt-other";
  other.name = `${name}-other`;
  other.maxLength = MAX_ANSWER_LENGTH;
  other.autocomplete = "off";

  let chosen: () => string[];
  if (isYesNo(question)) {
    // Two toggle buttons, Yes then No as the agent ordered them, and the Other answer (Yes or no).
    const group = element("div", "prompt-yes-no");
    group.setAttribute("role", "group");
    group.setAttribute("aria-label", question.question);
    const buttons = question.options.map((option) => {
      const toggle = button(option.label, "button", "prompt-toggle");
      toggle.setAttribute("aria-pressed", "false");
      toggle.addEventListener("click", () => {
        for (const b of buttons) {
          b.setAttribute("aria-pressed", String(b === toggle));
        }

        other.value = "";
      });
      return toggle;
    });
    group.append(...buttons);
    fieldset.append(group);
    chosen = () =>
      buttons.filter((b) => b.getAttribute("aria-pressed") === "true").map((b) => b.value);
    other.addEventListener("input", () => {
      if (other.value.trim()) {
        buttons.forEach((b) => b.setAttribute("aria-pressed", "false"));
      }
    });
  } else {
    const inputs = question.options.map((option) => {
      const label = element("label", "prompt-option");
      const input = document.createElement("input");
      input.type = question.multiSelect ? "checkbox" : "radio";
      input.name = name;
      input.value = option.label;
      if (!question.multiSelect) {
        input.addEventListener("change", () => {
          other.value = "";
        });
      }

      label.append(input, " ", element("span", "prompt-option-label", option.label));
      if (option.description) {
        label.append(" ", element("span", "prompt-option-description", option.description));
      }

      fieldset.append(label);
      if (option.previewHtml) {
        // Made safe on the server, as documents are (spec remote-answers, Safe rendering).
        const preview = element("div", "prompt-preview document");
        preview.innerHTML = option.previewHtml;
        fieldset.append(preview);
      }

      return input;
    });
    chosen = () => inputs.filter((i) => i.checked).map((i) => i.value);
    if (!question.multiSelect) {
      // Typing an Other answer to a single-choice question takes the place of a chosen option.
      other.addEventListener("input", () => {
        if (other.value.trim()) {
          inputs.forEach((i) => (i.checked = false));
        }
      });
    }
  }

  const otherLabel = element("label", "prompt-other-label");
  otherLabel.append(element("span", "", "Other"), " ", other);
  fieldset.append(otherLabel);

  return { fieldset, read: () => joinAnswer(chosen(), other.value, question.multiSelect) };
}

function permissionForm(session: SessionView, detail: PromptDetail, api: Api): HTMLFormElement {
  const form = baseForm(detail, "permission");
  const tool = session.prompt?.tool;
  form.append(element("p", "prompt-text", tool ? `Permission for ${tool}` : "Permission"));

  // A command, shown as text, never as HTML (spec remote-answers, Safe rendering).
  const preview = detail.preview;
  if (preview) {
    form.append(element("pre", "prompt-input", preview));
  } else {
    form.append(element("p", "prompt-pending", "Waiting for the tool's input."));
  }

  const messageLabel = element("label", "prompt-message-label");
  const message = document.createElement("textarea");
  message.className = "prompt-message";
  message.maxLength = MAX_MESSAGE_LENGTH;
  message.rows = 2;
  messageLabel.append(element("span", "", "Message for Deny (optional)"), " ", message);
  form.append(messageLabel);

  const actions = element("div", "prompt-actions");
  // Allow only once the input is there to read: no allowing what nobody has seen.
  const allow = button("Allow once", "button", "prompt-allow");
  allow.disabled = !preview;
  const deny = button("Deny", "button", "prompt-deny");
  actions.append(allow, deny);
  const { error, status } = messages(form);
  form.append(actions);

  allow.addEventListener("click", () => {
    if (preview) {
      void post(form, api, detail.id, { allow: true }, error, status);
    }
  });
  deny.addEventListener("click", () => {
    const text = message.value.trim();
    void post(
      form,
      api,
      detail.id,
      text ? { allow: false, message: text } : { allow: false },
      error,
      status,
    );
  });
  form.addEventListener("submit", (event) => event.preventDefault());

  return form;
}

/**
 * A visit request's form (spec remote-answers, Visit request form): the reason and action as text, the URL in full as
 * a link that opens in a new tab with no opener and no referrer, Done, Can't, and an optional note.
 */
function visitForm(detail: PromptDetail, visit: VisitRequest, api: Api): HTMLFormElement {
  const form = baseForm(detail, "visit");
  form.append(element("p", "prompt-text", visit.reason));
  if (visit.action) {
    form.append(element("p", "prompt-visit-action", visit.action));
  }

  const where = element("p", "prompt-visit-url");
  const href = visitHref(visit.url);
  if (href) {
    // The URL in full as the link's text, so the host is plain to see.
    const link = element("a", "prompt-visit-link", visit.url);
    link.href = href;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    where.append(link, element("span", "prompt-visit-hint", " (opens in a new tab)"));
  } else {
    where.append(element("span", "prompt-visit-link", visit.url));
  }

  form.append(where);

  const noteLabel = element("label", "prompt-message-label");
  const note = document.createElement("textarea");
  note.className = "prompt-message";
  note.maxLength = MAX_VISIT_NOTE_LENGTH;
  note.rows = 2;
  noteLabel.append(element("span", "", "Note (optional)"), " ", note);
  form.append(noteLabel);

  const actions = element("div", "prompt-actions");
  const done = button("Done", "button", "prompt-done");
  const cant = button("Can't", "button", "prompt-cant");
  actions.append(done, cant);
  const { error, status } = messages(form);
  form.append(actions);

  const answer = (visited: boolean): void => {
    const text = note.value.trim();
    void post(form, api, detail.id, text ? { visited, message: text } : { visited }, error, status);
  };
  done.addEventListener("click", () => answer(true));
  cant.addEventListener("click", () => answer(false));
  form.addEventListener("submit", (event) => event.preventDefault());

  return form;
}

async function post(
  form: HTMLFormElement,
  api: Api,
  promptId: string,
  body: PromptAnswerBody,
  error: HTMLElement,
  status: HTMLElement,
): Promise<void> {
  if (form.dataset["sending"] === "true") {
    return;
  }

  form.dataset["sending"] = "true";
  showError(error, "");
  setControls(form, false);
  status.textContent = "Sending…";
  try {
    await api.send("POST", `/api/prompts/${encodeURIComponent(promptId)}/answer`, body);
    status.textContent = "Answered. The session carries on.";
    form.dataset["answered"] = "true";
  } catch (failure) {
    status.textContent = "";
    showError(error, answerError(failure));
    // Still a prompt to answer only when the server didn't say it's gone or settled.
    const settled = failure instanceof ApiError && [403, 404, 409].includes(failure.status);
    setControls(form, !settled);
  } finally {
    delete form.dataset["sending"];
  }
}

function baseForm(detail: PromptDetail, kind: PromptKind): HTMLFormElement {
  const form = element("form", `prompt-form prompt-form--${kind}`);
  form.dataset["promptId"] = detail.id;
  form.noValidate = true;
  return form;
}

function messages(form: HTMLFormElement): { error: HTMLElement; status: HTMLElement } {
  const error = element("p", "prompt-error");
  error.setAttribute("role", "alert");
  error.hidden = true;
  const status = element("p", "prompt-status");
  status.setAttribute("aria-live", "polite");
  form.append(error, status);
  return { error, status };
}

function showError(error: HTMLElement, text: string): void {
  error.textContent = text;
  error.hidden = text === "";
}

function setControls(form: HTMLFormElement, enabled: boolean): void {
  for (const control of form.querySelectorAll<
    HTMLInputElement | HTMLButtonElement | HTMLTextAreaElement
  >("input, button, textarea")) {
    if (control.classList.contains("prompt-allow") && form.querySelector(".prompt-pending")) {
      control.disabled = true;
      continue;
    }

    control.disabled = !enabled;
  }
}

function button(text: string, type: "button" | "submit", className: string): HTMLButtonElement {
  const node = element("button", className, text);
  node.type = type;
  node.value = text;
  return node;
}

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className: string,
  text?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (className) {
    node.className = className;
  }

  if (text !== undefined) {
    node.textContent = text;
  }

  return node;
}
