import { agentName, relativeTime } from "./format";
import { renderProgressBar } from "./progressBar";
import { DEFAULT_QUIET, quietState, type QuietSettings, type QuietState } from "./quiet";
import type { ChangeCard } from "./types";
import { changeHref } from "./links";
import { leadingProjectLabel } from "./projectLabel";

/**
 * One change's card (spec live-dashboard, "Card content", "Two progress bars" and "Quiet cards"). All text goes in as
 * text.
 */
export function renderCard(
  card: ChangeCard,
  now: Date,
  quiet: QuietSettings = DEFAULT_QUIET,
): HTMLElement {
  const article = document.createElement("article");
  article.className = "card";
  article.dataset["changeId"] = card.changeId;

  // The Project's code plate leads the host line (web/DESIGN.md "Projects").
  const host = element("div", "card-host");
  host.append(...leadingProjectLabel(card), card.repo.host);

  const repoLine = element("div", "card-repo");
  repoLine.append(element("span", "card-repo-name", `${card.repo.owner}/${card.repo.name}`));
  // When it last reported, after its quiet state when it has one: "Idle, 1h ago", "Stale, last seen 5h ago".
  const when = element("span", "card-when");
  const state = element("span", "card-state");
  // A comma, not a middot (web/DESIGN.md, principle 5), and read aloud like the rest.
  const separator = element("span", "card-state-separator", ", ");
  const time = element("time", "card-time");
  time.dateTime = card.lastReportedAt;
  time.title = new Date(card.lastReportedAt).toLocaleString();
  when.append(state, separator, time);
  repoLine.append(when);

  // The change name is the card's link to its page (change-documents-and-history D11): an ordinary link, so a keyboard,
  // a screen reader and "open in new tab" all work. Wrapping the whole card would put the bars inside the link.
  const change = element("h2", "card-change");
  const link = element("a", "card-link", card.change);
  link.href = changeHref(card.changeId);
  link.dataset["route"] = "";
  change.append(link);

  const section = card.currentSection;
  const sectionBar = section
    ? renderProgressBar(
        `Section ${section.number}: ${section.title}`,
        section.progress,
        `Section ${section.number} progress`,
      )
    : renderProgressBar("No current section", { done: 0, total: 0 }, "Section progress");
  sectionBar.classList.add("bar-section");

  const overallBar = renderProgressBar("Whole change", card.overall, "Whole change progress");
  overallBar.classList.add("bar-overall");

  const agents = element("ul", "card-agents");
  agents.setAttribute("aria-label", "Agents");
  for (const agent of card.agents) {
    const row = element("li", "card-agent", agentName(agent));
    if (agent.member) {
      // The member, as its own quieter element after the agent (design D17). Set as text, never markup.
      row.append(" ", element("span", "card-agent-member", agent.member));
    }

    agents.append(row);
  }

  article.append(host, repoLine, change, sectionBar, overallBar, agents);
  applyQuiet(article, card, now, quiet);
  return article;
}

const STATE_LABELS: Record<QuietState, string> = { active: "", idle: "Idle", stale: "Stale" };

/**
 * Brings a card's quiet state and time up to date with `now` (design D2): the `card--idle` or `card--stale` class, the
 * state named in text (so the look isn't colour alone), and the time, which a stale card words as "last seen …".
 * Returns the state.
 */
export function applyQuiet(
  article: HTMLElement,
  card: ChangeCard,
  now: Date,
  quiet: QuietSettings,
): QuietState {
  const state = quietState(card.lastReportedAt, now, quiet);
  article.classList.toggle("card--idle", state === "idle");
  article.classList.toggle("card--stale", state === "stale");
  article.dataset["quiet"] = state;

  const label = article.querySelector<HTMLElement>(".card-state");
  const separator = article.querySelector<HTMLElement>(".card-state-separator");
  const time = article.querySelector<HTMLElement>(".card-time");
  if (label && separator) {
    label.textContent = STATE_LABELS[state];
    label.hidden = state === "active";
    separator.hidden = state === "active";
  }

  if (time) {
    const ago = relativeTime(card.lastReportedAt, now);
    time.textContent = state === "stale" ? `last seen ${ago}` : ago;
  }

  return state;
}

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className: string,
  text?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) {
    node.textContent = text;
  }

  return node;
}
