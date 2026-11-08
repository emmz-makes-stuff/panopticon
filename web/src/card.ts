import { agentName, relativeTime } from "./format";
import { renderProgressBar } from "./progressBar";
import type { ChangeCard } from "./types";
import { changeHref } from "./links";

/** One change's card (spec live-dashboard, "Card content" and "Two progress bars"). All text goes in as text. */
export function renderCard(card: ChangeCard, now: Date): HTMLElement {
  const article = document.createElement("article");
  article.className = "card";
  article.dataset["changeId"] = card.changeId;

  const host = element("div", "card-host", card.repo.host);

  const repoLine = element("div", "card-repo");
  repoLine.append(element("span", "card-repo-name", `${card.repo.owner}/${card.repo.name}`));
  const time = element("time", "card-time", relativeTime(card.lastReportedAt, now));
  time.dateTime = card.lastReportedAt;
  time.title = new Date(card.lastReportedAt).toLocaleString();
  repoLine.append(time);

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
  return article;
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
