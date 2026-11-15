import { repoWebUrl } from "./links";
import type { CardRepo } from "./types";

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

/**
 * A Copy button and its status line, in a `.copy-row`. `text` is read on each click, so it can change after the row
 * is made; `clear` empties the status, for when the text does.
 */
export function copyRow(
  copy: (text: string) => Promise<void>,
  text: () => string,
): { element: HTMLDivElement; clear(): void } {
  const status = el("span", "copy-status");
  status.setAttribute("role", "status");
  const copyButton = button("Copy", () => {
    copy(text()).then(
      () => {
        status.textContent = "Copied.";
      },
      () => {
        status.textContent = "Couldn't copy. Select the text and copy it.";
      },
    );
  });
  return {
    element: el("div", "copy-row", copyButton, status),
    clear: () => {
      status.textContent = "";
    },
  };
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

/**
 * The repo's `owner/name` as a link to its web page, in a new tab (spec repos, Repo links; repos-page D3). It's an
 * ordinary link, not a `data-route` one, so the router leaves it to the browser.
 */
export function repoLink(repo: CardRepo, className?: string): HTMLAnchorElement | HTMLSpanElement {
  const text = `${repo.owner}/${repo.name}`;
  const href = repoWebUrl(repo);
  if (href === null) {
    // A host that isn't a plain hostname gets plain text, not a link (see repoWebUrl).
    return el("span", className, text);
  }

  const link = el("a", className ? `${className} repo-link` : "repo-link", text);
  link.href = href;
  link.target = "_blank";
  link.rel = "noopener noreferrer";
  link.title = `Open ${repo.owner}/${repo.name} on ${repo.host}`;
  // Read aloud as "owner/name, opens in a new tab"; the arrow is only for the eye (repos-page D4).
  link.setAttribute("aria-label", `${text}, opens in a new tab`);
  link.append(externalMark());
  return link;
}

const SVG_NS = "http://www.w3.org/2000/svg";

/** A small arrow out of a box: the link leaves the dashboard. Decorative, drawn in the text colour. */
function externalMark(): SVGSVGElement {
  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("class", "external-mark");
  svg.setAttribute("viewBox", "0 0 12 12");
  svg.setAttribute("aria-hidden", "true");
  svg.setAttribute("focusable", "false");
  const path = document.createElementNS(SVG_NS, "path");
  path.setAttribute(
    "d",
    "M7 1.5h3.5V5M10.5 1.5 5.5 6.5M9 7.5v2.5a.5.5 0 0 1-.5.5h-6.5a.5.5 0 0 1-.5-.5v-6.5a.5.5 0 0 1 .5-.5H5",
  );
  svg.append(path);
  return svg;
}
