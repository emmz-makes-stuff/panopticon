import { answerList, withAnswer } from "../answerList";
import { leadingProjectLabel } from "../projectLabel";
import { tagPicker } from "../tagPicker";
import { ApiError, fetchChange, fetchChangeMetrics, fetchDocument } from "../api";
import { el, page, routeLink } from "../dom";
import { agentName, calendarDate, relativeTime } from "../format";
import { changeHref } from "../links";
import {
  ANSWER_RECORDED,
  AWAY_CHANGED,
  CHANGE_ARCHIVED,
  CHANGE_UPDATED,
  PROJECT_TAGS_CHANGED,
  PROJECTS_CHANGED,
  parseAnswerEntry,
  parseArchivedId,
  parseAwayState,
  parseCard,
  parseProjectsChange,
  parseProjectTagsChange,
  retagCard,
} from "../live";
import { renderChangeMetrics } from "../metricsPanel";
import { renderProgressBar } from "../progressBar";
import type { View } from "../router";
import type { Env } from "../shell";
import type { ChangePage, DocumentEntry, DocumentKind } from "../types";

/** The document a page opens at without `?doc=`: the proposal, else the tasks, else the first one. */
export function defaultDocument(documents: readonly DocumentEntry[]): DocumentEntry | undefined {
  return (
    documents.find((d) => d.kind === "proposal") ??
    documents.find((d) => d.kind === "tasks") ??
    documents[0]
  );
}

/** How the document list is grouped, in the server's order within each group. */
const GROUPS: readonly { title: string; kinds: readonly DocumentKind[] }[] = [
  { title: "Change", kinds: ["proposal", "design", "tasks"] },
  { title: "Specs", kinds: ["spec"] },
  { title: "Devlog", kinds: ["devlog"] },
  { title: "Other files", kinds: ["meta", "other"] },
];

const failureText = (failure: unknown) =>
  failure instanceof ApiError ? failure.message : "Couldn't reach the server. Try again.";

/**
 * `/changes/<id>` (spec change-documents, design D11): the change's header, its documents as route links
 * (`?doc=<path>`, so a document is linkable and survives a reload), and the selected one rendered. An active change's
 * page follows the live stream for that change alone: a report refetches what's shown, and an archive says so.
 */
export function changeView(env: Env): View {
  return async ({ root, route, params }) => {
    const changeId = route["id"] ?? "";
    let requested = params.get("doc");
    let ticket = 0;
    let closeStream: (() => void) | undefined;

    const head = el("header", "change-head");
    // One for the page's life: a live refresh re-renders the header around it, and an open panel stays open.
    // The card each tag change returns replaces the page's, so choosing another document doesn't put old chips back.
    const tags = tagPicker(env, changeId, (card) => {
      latest = { ...latest, card };
    });
    const nav = el("nav", "doc-nav");
    nav.setAttribute("aria-label", "Documents");
    const notice = el("p", "doc-notice");
    notice.setAttribute("role", "status");
    const article = el("article", "doc");
    article.setAttribute("aria-live", "polite");
    const body = el("div", "change-body", nav, el("div", "doc-pane", notice, article));
    // change-metrics D5: under the header, loaded with the page and again on each live refresh.
    const metricsPanel = el("section", "change-metrics");
    metricsPanel.setAttribute("aria-label", "Metrics");
    let metricsTicket = 0;
    const archivedStatus = el("p", "change-archived-status");
    archivedStatus.setAttribute("role", "status");
    // agent-interaction D8: answers given from the dashboard, kept live by `answer-recorded`.
    const answers = answerList(env.now);

    let first: ChangePage;
    try {
      first = await fetchChange(env.api, changeId);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 401) {
        return undefined; // The shell is already on its way to sign-in, and comes back here after.
      }

      root.replaceChildren(
        failure instanceof ApiError && failure.status === 404 ? notFound() : loadError(failure),
      );
      return undefined;
    }

    root.replaceChildren(
      el("section", "page change-page", head, metricsPanel, archivedStatus, answers.element, body),
    );
    let latest = first;
    void loadMetrics();
    await show(first, ++ticket);
    if (!first.archivedAt) {
      closeStream = follow();
    }

    return {
      teardown: () => {
        ticket++;
        metricsTicket++;
        closeStream?.();
        closeStream = undefined;
      },
      // Another document of this change (a `?doc=` link): only the list and the article change, from the page already
      // loaded, so the header isn't refetched and the live stream stays open.
      update: (next) => {
        requested = next.get("doc");
        void show(latest, ++ticket);
      },
    };

    // --- rendering ---

    async function show(changePage: ChangePage, mine: number): Promise<void> {
      latest = changePage;
      head.replaceChildren(...renderHead(changePage));
      answers.render(changePage.answers ?? []);
      const listed = changePage.documents;
      const wanted = requested ? listed.find((d) => d.path === requested) : undefined;
      const selected = wanted ?? defaultDocument(listed);
      nav.replaceChildren(...renderNav(listed, selected?.path));
      notice.textContent =
        requested && !wanted && selected
          ? `This change has no ${requested}, so here is its ${selected.title.toLowerCase()} instead.`
          : "";

      if (!selected) {
        article.replaceChildren(el("p", "doc-empty", "No documents have been synced yet."));
        return;
      }

      try {
        const rendered = await fetchDocument(env.api, changeId, selected.path);
        if (mine !== ticket) {
          return; // A newer refresh, or leaving the page, overtook this one.
        }

        // The one place the app takes HTML: the server renders documents with raw HTML disabled, unsafe link and
        // image targets removed and images turned into links (server: Documents/DocumentRenderer, design D8), and
        // only this endpoint's answer is ever inserted.
        article.innerHTML = rendered.html;
        article.dataset["path"] = rendered.path;
      } catch (failure) {
        if (mine === ticket) {
          article.replaceChildren(el("p", "form-error", failureText(failure)));
          article.firstElementChild?.setAttribute("role", "alert");
        }
      }
    }

    function renderHead(changePage: ChangePage): Node[] {
      const { card } = changePage;
      const back = changePage.archivedAt
        ? routeLink("/history", "Back to history", "back-link")
        : routeLink("/", "Back to the dashboard", "back-link");
      const repo = el(
        "div",
        "change-repo",
        ...leadingProjectLabel(card),
        el("span", "change-host", card.repo.host),
        " ",
        el("span", "change-repo-name", `${card.repo.owner}/${card.repo.name}`),
      );
      const when = changePage.archivedAt
        ? el("p", "change-when archived", `Archived ${calendarDate(changePage.archivedAt)}`)
        : el("p", "change-when", `Last report ${relativeTime(card.lastReportedAt, env.now())}`);
      const bars = el("div", "change-bars");
      if (card.currentSection && !changePage.archivedAt) {
        const section = card.currentSection;
        bars.append(
          renderProgressBar(
            `Section ${section.number}: ${section.title}`,
            section.progress,
            `Section ${section.number} progress`,
          ),
        );
      }

      bars.append(renderProgressBar("Whole change", card.overall, "Whole change progress"));
      const agents = el("ul", "card-agents change-agents");
      agents.setAttribute("aria-label", "Agents");
      for (const agent of card.agents) {
        const row = el("li", "card-agent", agentName(agent));
        if (agent.member) {
          row.append(" ", el("span", "card-agent-member", agent.member));
        }

        agents.append(row);
      }

      // The tags sit under the change name (web/DESIGN.md "Tags"); no tags and no picker, no row.
      tags.update(card);
      return [back, repo, el("h2", "change-name", card.change), tags.element, when, bars, agents];
    }

    function renderNav(documents: readonly DocumentEntry[], selected: string | undefined): Node[] {
      return GROUPS.flatMap((group) => {
        const members = documents.filter((d) => group.kinds.includes(d.kind));
        if (members.length === 0) {
          return [];
        }

        const list = el("ul", "doc-list");
        for (const document of members) {
          const link = routeLink(changeHref(changeId, document.path), document.title, "doc-link");
          link.title = document.path;
          if (document.path === selected) {
            link.setAttribute("aria-current", "page");
          }

          list.append(el("li", `doc-item doc-${document.kind}`, link));
        }

        return [el("h3", "doc-group", group.title), list];
      });
    }

    async function loadMetrics(): Promise<void> {
      const mine = ++metricsTicket;
      try {
        const metrics = await fetchChangeMetrics(env.api, changeId);
        if (mine === metricsTicket) {
          metricsPanel.replaceChildren(...renderChangeMetrics(metrics));
        }
      } catch (failure) {
        // The panel keeps what it shows; the next refresh tries again. A 401 is the shell's to handle.
        const signedOut = failure instanceof ApiError && failure.status === 401;
        if (mine === metricsTicket && !signedOut && metricsPanel.childElementCount === 0) {
          metricsPanel.replaceChildren(
            el("p", "metrics-error", "Couldn't load this change's metrics."),
          );
        }
      }
    }

    // --- live ---

    function follow(): () => void {
      const create = env.createEventSource ?? ((url: string) => new EventSource(url));
      const source = create("/api/events");
      let opened = false;

      // Its own ticket: a document picked while a refresh is loading must not drop the refreshed header.
      let refreshing = 0;
      const refresh = () => {
        const mine = ++refreshing;
        void loadMetrics();
        fetchChange(env.api, changeId).then(
          (next) => {
            if (mine !== refreshing || closeStream === undefined) {
              return; // A newer refresh overtook this one, or the page was left.
            }

            if (next.archivedAt) {
              archived(next);
            }

            return show(next, ++ticket);
          },
          (failure: unknown) => {
            // Gone from this viewer's scope (projects D3, §11): the change isn't found, as it would be on a reload.
            if (
              mine === refreshing &&
              closeStream !== undefined &&
              failure instanceof ApiError &&
              failure.status === 404
            ) {
              ticket++;
              metricsTicket++;
              closeStream();
              closeStream = undefined;
              root.replaceChildren(notFound());
            }

            // Otherwise the page keeps what it shows; the next event or reconnect tries again.
          },
        );
      };

      // A reconnect may have missed events, so catch up then; the first open needs nothing.
      source.onopen = () => {
        if (opened) {
          refresh();
        }

        opened = true;
      };
      source.onerror = () => {
        // The browser retries by itself. If it gives up, the page keeps its documents without live updates.
      };
      source.addEventListener(CHANGE_UPDATED, (event) => {
        if (parseCard(event.data)?.changeId === changeId) {
          refresh();
        }
      });
      source.addEventListener(CHANGE_ARCHIVED, (event) => {
        if (parseArchivedId(event.data) === changeId) {
          refresh();
        }
      });
      // spec change-visibility, "Removed while on a change page": leaving this change's Project makes it not found.
      source.addEventListener(PROJECTS_CHANGED, (event) => {
        const change = parseProjectsChange(event.data);
        const project = latest.card.project?.id.toLowerCase();
        if (change && !change.member && project === change.projectId.toLowerCase()) {
          refresh();
        }
      });

      // tags D6: a rename or delete in this change's Project shows at once, with no refetch of the page. The chips are
      // the picker's, so it retags them (and reloads the Project's tags, so an added one is offered); the page keeps its
      // copy current for the next header render.
      source.addEventListener(PROJECT_TAGS_CHANGED, (event) => {
        const change = parseProjectTagsChange(event.data);
        const project = latest.card.project?.id.toLowerCase();
        if (!change || project !== change.projectId.toLowerCase()) {
          return;
        }

        latest = { ...latest, card: retagCard(latest.card, change) };
        tags.projectTagsChanged(change);
      });

      source.addEventListener(ANSWER_RECORDED, (event) => {
        const entry = parseAnswerEntry(event.data);
        if (entry?.changeId.toLowerCase() === changeId.toLowerCase()) {
          latest = { ...latest, answers: withAnswer(latest.answers ?? [], entry) };
          answers.render(latest.answers ?? []);
        }
      });

      // The header's away switch follows this stream too, while it's the one open.
      source.addEventListener(AWAY_CHANGED, (event) => {
        const state = parseAwayState(event.data);
        if (state) {
          env.awayChanged(state);
        }
      });

      return () => source.close();
    }

    function archived(next: ChangePage): void {
      archivedStatus.textContent = `This change has been archived${next.archiveFolder ? ` as ${next.archiveFolder}` : ""}. It's in the history now; its documents stay here.`;
      closeStream?.();
      closeStream = undefined;
    }
  };
}

function notFound(): HTMLElement {
  return page(
    "Change not found",
    el(
      "p",
      undefined,
      "There's no change with this address. The link may be wrong, or the change may be from another server.",
    ),
    routeLink("/", "Back to the dashboard"),
  );
}

function loadError(failure: unknown): HTMLElement {
  const section = el(
    "section",
    "load-error",
    el("h2", undefined, "Couldn't load this change"),
    el("p", undefined, `${failureText(failure)} Check that the server is running, then reload.`),
  );
  section.setAttribute("role", "alert");
  return section;
}
