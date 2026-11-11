import type { Api } from "./api";
import { renderPromptForm } from "./promptForm";
import type { ChangeCard, SessionView } from "./types";

/**
 * A card's session rows (spec live-dashboard, "Sessions on a card"; agent-interaction D10): who and where, then what
 * the session is doing or waiting on, and for how long, all in text. "Emmz on mbp: Run make gates, just now". Commas,
 * not middots (web/DESIGN.md, principle 5; Emmz, 2026-11-11).
 *
 * Rows are patched in place, keyed by session id, so a live event never rebuilds the card: a row whose session hasn't
 * changed keeps its element, and only its time moves on with the clock.
 */

/**
 * Who is looking at the card, and how they answer from it: `id` decides "you" against a member's name, and `api`
 * posts a held prompt's answer. Without `api`, a held prompt's form isn't offered.
 */
export interface SessionViewer {
  id?: string | undefined;
  api?: Api | undefined;
}

/** What the session is doing, or waiting on, from `viewerId`'s side: "you" is only ever the session's own member. */
export function sessionText(session: SessionView, viewerId: string | undefined): string {
  switch (session.state) {
    case "working":
      return session.activity?.trim() || "working";
    case "waiting-on-user":
      return sameId(session.memberId, viewerId)
        ? "waiting for you"
        : `waiting for ${session.memberName}`;
    case "waiting-on-question":
      return "waiting on a question";
    case "waiting-on-permission":
      return session.prompt?.tool
        ? `waiting on permission for ${session.prompt.tool}`
        : "waiting on permission";
  }
}

/** How long since `iso`, as the row says it: "just now", "4m", "2h", "3d". */
export function elapsed(iso: string, now: Date): string {
  const seconds = Math.max(0, Math.floor((now.getTime() - Date.parse(iso)) / 1000));
  if (seconds < 60) {
    return "just now";
  }

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m`;
  }

  const hours = Math.floor(minutes / 60);
  return hours < 24 ? `${hours}h` : `${Math.floor(hours / 24)}d`;
}

/**
 * Who a waiting session needs, from `viewerId`'s side (spec live-dashboard, Needs you): "you" when the viewer may
 * answer (the server sends the prompt's detail only to them) or is the session's member; otherwise the member's name.
 * Null when it needs nobody: working, or waiting after its turn ended.
 */
export function needs(session: SessionView, viewerId: string | undefined): string | null {
  if (session.state !== "waiting-on-question" && session.state !== "waiting-on-permission") {
    return null;
  }

  if (session.promptDetail || sameId(session.memberId, viewerId)) {
    return "Needs you";
  }

  const prompt = session.prompt;
  if (!prompt) {
    // Over the prompt limits: nothing about the prompt reached the server but that it waits.
    return `Needs ${session.memberName}`;
  }

  const what =
    prompt.kind === "question"
      ? "question"
      : prompt.tool
        ? `permission for ${prompt.tool}`
        : "permission";
  return `Needs ${session.memberName}: ${what}`;
}

/** A prompt held for the dashboard that this viewer may answer (spec live-dashboard, Needs-you count, Browser notifications). */
export interface HeldPrompt {
  promptId: string;
  changeId: string;
  change: string;
  kind: "question" | "permission";
  tool?: string | null | undefined;
}

/**
 * Every held prompt on `cards` that the viewer may answer: the server sends a prompt's detail only to them (D5), so
 * it's each session whose detail says it's held. In card order, then session order.
 */
export function heldPrompts(cards: Iterable<ChangeCard>): HeldPrompt[] {
  const held: HeldPrompt[] = [];
  for (const card of cards) {
    for (const session of card.sessions ?? []) {
      const detail = session.promptDetail;
      if (detail?.held) {
        held.push({
          promptId: detail.id,
          changeId: card.changeId,
          change: card.change,
          kind: detail.questions?.length ? "question" : "permission",
          tool: session.prompt?.tool,
        });
      }
    }
  }

  return held;
}

/** The card's last activity: its last report, or a session's last event if that's later. */
export function lastActiveAt(card: ChangeCard): string {
  return card.lastActiveAt ?? card.lastReportedAt;
}

/**
 * Brings the card's session list in line with `card.sessions`, in the server's order (oldest first): new rows are
 * added, gone ones removed, and changed ones replaced. Unchanged rows stay as they are. No sessions, no list.
 */
export function updateSessions(
  article: HTMLElement,
  card: ChangeCard,
  now: Date,
  viewer: SessionViewer = {},
): void {
  const viewerId = viewer.id;
  const sessions = card.sessions ?? [];
  // For the tab title's count (6.3) and the card's look: some session here needs this viewer.
  article.classList.toggle(
    "card--needs-you",
    sessions.some((session) => needs(session, viewerId) === "Needs you"),
  );
  let list = article.querySelector<HTMLUListElement>(".card-sessions");
  if (sessions.length === 0) {
    list?.remove();
    return;
  }

  if (!list) {
    list = document.createElement("ul");
    list.className = "card-sessions";
    list.setAttribute("aria-label", "Sessions");
    const agents = article.querySelector(".card-agents");
    if (agents) {
      agents.after(list);
    } else {
      article.append(list);
    }
  }

  const existing = new Map<string, HTMLElement>();
  for (const row of list.querySelectorAll<HTMLElement>(":scope > .card-session")) {
    existing.set(row.dataset["sessionId"] ?? "", row);
  }

  const rows = sessions.map((session) => {
    const signature = rowSignature(session, viewerId);
    const current = existing.get(session.id);
    if (current?.dataset["signature"] === signature) {
      return current;
    }

    const row = renderRow(session, viewer);
    row.dataset["signature"] = signature;
    return row;
  });

  if (
    rows.length !== list.children.length ||
    rows.some((row, index) => list.children[index] !== row)
  ) {
    list.replaceChildren(...rows);
  }

  updateSessionTimes(article, now);
}

/**
 * Moves each prompt form of `from` that `to` shows too (the same prompt id) into `to`, in place of its fresh copy:
 * a new report rebuilds a card, and a half-typed answer shouldn't go with it.
 */
export function keepPromptForms(from: HTMLElement, to: HTMLElement): void {
  for (const fresh of to.querySelectorAll<HTMLFormElement>(".prompt-form[data-prompt-id]")) {
    const id = fresh.dataset["promptId"];
    const kept = [...from.querySelectorAll<HTMLFormElement>(".prompt-form[data-prompt-id]")].find(
      (form) => form.dataset["promptId"] === id,
    );
    if (kept) {
      fresh.replaceWith(kept);
    }
  }
}

/** Moves every session row's elapsed time on to `now` (the quiet clock's tick). */
export function updateSessionTimes(article: HTMLElement, now: Date): void {
  for (const time of article.querySelectorAll<HTMLTimeElement>(".card-session-time")) {
    time.textContent = elapsed(time.dateTime, now);
  }
}

function renderRow(session: SessionView, viewer: SessionViewer): HTMLElement {
  const viewerId = viewer.id;
  const row = document.createElement("li");
  row.className = "card-session";
  row.dataset["sessionId"] = session.id;
  row.dataset["state"] = session.state;
  const line = span("card-session-line", "");

  // Names and activity are text, never markup. The activity is what shows; who and where go in the time's tooltip,
  // with the full timestamp (Emmz, 2026-11-11). Screen readers still hear the whole row, from the hidden text:
  // "Emmz on mbp: Run make gates, just now".
  const whoText = `${session.memberName} on ${session.machine}`;
  const who = span("card-session-who visually-hidden", `${whoText}: `);
  const what = span("card-session-what", sessionText(session, viewerId));
  const time = document.createElement("time");
  time.className = "card-session-time";
  time.dateTime = session.since;
  time.title = `${whoText}, ${new Date(session.since).toLocaleString()}`;
  line.append(who, what, span("visually-hidden", ","), " ", time);
  row.append(line);

  const marker = needs(session, viewerId);
  if (marker) {
    row.append(needsBlock(session, marker, viewer));
  }

  return row;
}

/**
 * The "Needs …" marker, and under "Needs you" the prompt: its form while it's held and the viewer can answer here, or
 * else the question texts and "answer in the terminal". Everything but a preview is text.
 */
function needsBlock(session: SessionView, marker: string, viewer: SessionViewer): HTMLElement {
  const block = document.createElement("div");
  block.className = marker === "Needs you" ? "card-session-needs needs-you" : "card-session-needs";
  const label = document.createElement("strong");
  label.className = "card-session-marker";
  label.textContent = marker;
  block.append(label);

  const detail = session.promptDetail;
  if (!detail) {
    return block;
  }

  if (detail.held && viewer.api) {
    block.append(renderPromptForm(session, detail, viewer.api));
    return block;
  }

  if (detail.questions?.length) {
    const list = document.createElement("ul");
    list.className = "card-session-questions";
    for (const question of detail.questions) {
      list.append(span("card-session-question", question.question, "li"));
    }

    block.append(list);
  } else {
    const tool = session.prompt?.tool;
    block.append(
      " ",
      span("card-session-question", tool ? `permission for ${tool}` : "permission"),
    );
  }

  block.append(" ", span("card-session-terminal", "answer in the terminal"));
  return block;
}

// Everything a row shows but its time: a row is rebuilt only when this changes.
function rowSignature(session: SessionView, viewerId: string | undefined): string {
  return JSON.stringify([
    session.memberName,
    session.machine,
    session.since,
    sessionText(session, viewerId),
    needs(session, viewerId),
    session.prompt ?? null,
    session.promptDetail ?? null,
  ]);
}

function span(className: string, text: string, tag: "span" | "li" = "span"): HTMLElement {
  const node = document.createElement(tag);
  node.className = className;
  node.textContent = text;
  return node;
}

function sameId(a: string, b: string | undefined): boolean {
  return b !== undefined && a.toLowerCase() === b.toLowerCase();
}
