/** A child of `el`: a node, or text (always set as text, never parsed as markup). */
export type Child = Node | string | null | undefined | false;

/**
 * Makes an element with a class and children. Strings become text nodes, so names and messages from the server can
 * never inject markup.
 */
export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
  ...children: Child[]
): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  if (className) {
    element.className = className;
  }

  for (const child of children) {
    if (child) {
      element.append(child);
    }
  }

  return element;
}

/** A button of `type="button"` that runs `onClick`. */
export function button(text: string, onClick: () => void, className?: string): HTMLButtonElement {
  const element = el("button", className, text);
  element.type = "button";
  element.addEventListener("click", onClick);
  return element;
}

/** An alert line for errors; empty (and hidden from the accessibility tree) until a message is set. */
export function alertLine(): {
  element: HTMLParagraphElement;
  show(message: string): void;
  clear(): void;
} {
  const element = el("p", "form-error");
  element.setAttribute("role", "alert");
  return {
    element,
    show: (message) => {
      element.textContent = message;
    },
    clear: () => {
      element.textContent = "";
    },
  };
}

/** A page section with a heading, as every non-card view starts. */
export function page(title: string, ...children: Child[]): HTMLElement {
  return el("section", "page", el("h2", undefined, title), ...children);
}

/** A labelled text input. */
export function field(
  label: string,
  input: Partial<
    Pick<HTMLInputElement, "type" | "name" | "autocomplete" | "required" | "value" | "maxLength">
  >,
): { element: HTMLLabelElement; input: HTMLInputElement } {
  const control = document.createElement("input");
  Object.assign(control, input);
  const element = el("label", "field", el("span", "field-label", label), control);
  return { element, input: control };
}

/** A labelled select of `[value, text]` options, with `value` chosen. */
export function selectField(
  label: string,
  name: string,
  options: readonly (readonly [value: string, text: string])[],
  value: string,
): { element: HTMLLabelElement; select: HTMLSelectElement } {
  const select = document.createElement("select");
  select.name = name;
  for (const [optionValue, text] of options) {
    const option = el("option", undefined, text);
    option.value = optionValue;
    select.append(option);
  }

  select.value = value;
  const element = el("label", "field", el("span", "field-label", label), select);
  return { element, select };
}

/** A link the router handles in place (`<a data-route>`). */
export function routeLink(href: string, text: string, className?: string): HTMLAnchorElement {
  const link = el("a", className, text);
  link.href = href;
  link.dataset["route"] = "";
  return link;
}
