import { el } from "./dom";
import { relativeTime } from "./format";
import type { AnswerEntry } from "./types";

/**
 * The change page's list of answers given from the dashboard (spec remote-answers, Answers are attributed;
 * agent-interaction D8): who answered whose session, when, and what. The question texts and labels come only to a
 * viewer who could have answered; everyone else sees who answered and the kind. Everything is text.
 */
export interface AnswerList {
  element: HTMLElement;
  /** Shows `entries`, newest first (the server's order). No answers, no section. */
  render(entries: readonly AnswerEntry[]): void;
}

export function answerList(now: () => Date): AnswerList {
  const list = el("ul", "answer-list");
  const element = el(
    "section",
    "change-answers",
    el("h3", "answers-title", "Answers from the dashboard"),
    list,
  );
  element.setAttribute("aria-label", "Answers from the dashboard");
  element.hidden = true;

  return {
    element,
    render(entries) {
      element.hidden = entries.length === 0;
      list.replaceChildren(...entries.map((entry) => renderEntry(entry, now())));
    },
  };
}

/** What one answer says, as the spec words it: "Bob answered a question for Emmz", "Bob allowed Bash for Emmz". */
export function answerSummary(entry: AnswerEntry): string {
  const member = entry.sessionMember;
  if (entry.kind === "question") {
    return `${entry.answeredBy} answered a question for ${member}`;
  }

  const verb = entry.outcome === "allow" ? "allowed" : "denied";
  return `${entry.answeredBy} ${verb} ${entry.tool ?? "a tool"} for ${member}`;
}

/** `entry` added to `entries` in its place, newest first, once only. */
export function withAnswer(entries: readonly AnswerEntry[], entry: AnswerEntry): AnswerEntry[] {
  const others = entries.filter((e) => e.id !== entry.id);
  return [...others, entry].sort(
    (a, b) => Date.parse(b.answeredAt) - Date.parse(a.answeredAt) || b.id.localeCompare(a.id),
  );
}

function renderEntry(entry: AnswerEntry, now: Date): HTMLElement {
  const time = el("time", "answer-time", relativeTime(entry.answeredAt, now));
  time.dateTime = entry.answeredAt;
  time.title = new Date(entry.answeredAt).toLocaleString();
  const item = el(
    "li",
    `answer answer--${entry.kind}`,
    el("span", "answer-summary", answerSummary(entry)),
    ", ",
    time,
  );
  item.dataset["answerId"] = entry.id;

  if (entry.questions?.length) {
    const questions = el("dl", "answer-questions");
    for (const question of entry.questions) {
      questions.append(
        el("dt", "answer-question", question.question),
        el("dd", "answer-given", question.answer),
      );
    }

    item.append(questions);
  }

  return item;
}
