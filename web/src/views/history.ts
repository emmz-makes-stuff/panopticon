import { ApiError, fetchHistory } from "../api";
import { alertLine, button, el, page, routeLink } from "../dom";
import { agentName, calendarDate } from "../format";
import type { View } from "../router";
import type { Env } from "../shell";
import type { HistoryEntry } from "../types";
import { changeHref } from "../links";

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/**
 * `/history` (spec change-history, design D11): archived changes, the most recently archived first, each opening its
 * change page at the final snapshot. "Show more" fetches the next page.
 */
export function historyView(env: Env): View {
  return async ({ root }) => {
    const list = el("ol", "history-list");
    const error = alertLine();
    const more = button("Show more", () => void load(), "show-more");
    more.hidden = true;
    let before: string | null = null;
    let left = false;

    async function load(): Promise<void> {
      more.disabled = true;
      try {
        const next = await fetchHistory(env.api, before);
        if (left) {
          return;
        }

        if (!before && next.changes.length === 0) {
          list.replaceWith(empty());
        }

        list.append(...next.changes.map(row));
        before = next.nextBefore;
        more.hidden = before === null;
        more.textContent = "Show more";
        error.clear();
      } catch (failure) {
        if (!(failure instanceof ApiError && failure.status === 401)) {
          error.show(failureText(failure));
          // A failed first load leaves nothing to show more of, so the button offers to try again.
          if (before === null) {
            more.textContent = "Try again";
            more.hidden = false;
          }
        }
      } finally {
        more.disabled = false;
      }
    }

    root.replaceChildren(page("History", error.element, list, more));
    await load();
    return () => {
      left = true;
    };
  };
}

function row(entry: HistoryEntry): HTMLLIElement {
  const archived = el("time", "history-date", `Archived ${calendarDate(entry.archivedAt)}`);
  archived.dateTime = entry.archivedAt;
  const agents = el("ul", "card-agents history-agents");
  agents.setAttribute("aria-label", "Agents");
  for (const agent of entry.agents) {
    const item = el("li", "card-agent", agentName(agent));
    if (agent.member) {
      item.append(" ", el("span", "card-agent-member", agent.member));
    }

    agents.append(item);
  }

  return el(
    "li",
    "history-item",
    el("h3", "history-change", routeLink(changeHref(entry.changeId), entry.change, "history-link")),
    el(
      "p",
      "history-meta",
      el("span", "history-repo", `${entry.repo.host} ${entry.repo.owner}/${entry.repo.name}`),
      " ",
      archived,
    ),
    agents,
  );
}

function empty(): HTMLElement {
  return el(
    "div",
    "history-empty",
    el("p", undefined, "No changes have been archived yet."),
    el(
      "p",
      undefined,
      "When a change is archived with /opsx:archive, it leaves the dashboard and appears here, with its final documents.",
    ),
  );
}
