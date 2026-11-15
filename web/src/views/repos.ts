import { ApiError, fetchRepos } from "../api";
import { alertLine, button, el, page, repoLink, routeLink } from "../dom";
import { repoValue } from "../filters";
import { calendarDate, relativeTime } from "../format";
import { changeHref } from "../links";
import { leadingProjectLabel } from "../projectLabel";
import type { View } from "../router";
import type { Env } from "../shell";
import type { RepoActiveChange, RepoArchivedChange, RepoSummary } from "../types";

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/**
 * `/repos` (spec repos, repos-page D5): every repo with a change the member can see, newest activity first. Under
 * each repo are its active changes, then its newest archived ones, and a link to History for the rest. It loads once
 * per visit, like History: no live updates (Emmz, 2026-11-15).
 */
export function reposView(env: Env): View {
  return async ({ root }) => {
    const body = el("div", "repos-body");
    const error = alertLine();
    const retry = button("Try again", () => void load(), "show-more");
    retry.hidden = true;
    let left = false;

    async function load(): Promise<void> {
      retry.disabled = true;
      try {
        const { repos } = await fetchRepos(env.api);
        if (left) {
          return;
        }

        body.replaceChildren(
          repos.length === 0
            ? empty()
            : el("div", "repos-list", ...repos.map((r) => repoSection(r, env.now()))),
        );
        error.clear();
        retry.hidden = true;
      } catch (failure) {
        // The shell answers a 401 by showing the sign-in page and coming back here (spec repos, "Signed out").
        if (!left && !(failure instanceof ApiError && failure.status === 401)) {
          error.show(failureText(failure));
          retry.hidden = false;
        }
      } finally {
        retry.disabled = false;
      }
    }

    root.replaceChildren(page("Repos", error.element, body, retry));
    await load();
    return () => {
      left = true;
    };
  };
}

function repoSection(summary: RepoSummary, now: Date): HTMLElement {
  const changes = el(
    "ul",
    "repo-changes",
    ...summary.active.map((change) =>
      changeItem(
        change,
        `Reported ${relativeTime(change.lastReportedAt, now)}`,
        change.lastReportedAt,
      ),
    ),
    ...summary.archived.map((change) =>
      changeItem(change, `Archived ${calendarDate(change.archivedAt)}`, change.archivedAt),
    ),
  );
  if (summary.moreArchived > 0) {
    // The rest are in History, filtered to this repo (spec repos, "More than five archived").
    const query = new URLSearchParams({ repo: repoValue(summary.repo) }).toString();
    changes.append(
      el(
        "li",
        "repo-more",
        routeLink(`/history?${query}`, `${summary.moreArchived} more in History`),
      ),
    );
  }

  return el(
    "section",
    "repo",
    el(
      "h3",
      "repo-heading",
      el("span", "repo-host", summary.repo.host),
      " ",
      repoLink(summary.repo, "repo-name"),
    ),
    changes,
  );
}

function changeItem(
  change: RepoActiveChange | RepoArchivedChange,
  when: string,
  at: string,
): HTMLLIElement {
  const time = el("time", "repo-change-when", when);
  time.dateTime = at;
  return el(
    "li",
    "repo-change",
    routeLink(changeHref(change.changeId), change.change, "repo-change-link"),
    el("span", "repo-change-meta", ...leadingProjectLabel(change), time),
  );
}

function empty(): HTMLElement {
  return el(
    "div",
    "repos-empty",
    el("p", undefined, "No repos yet."),
    el("p", undefined, "A repo appears here once an agent reports progress on one of its changes."),
  );
}
