import type { App } from "./app";
import type { AnswerEntry, AwayState, ChangeCard, ProjectTagRef, SessionView } from "./types";

/** The SSE event the server sends for every changed card (server: Dashboard/DashboardEndpoints). */
export const CHANGE_UPDATED = "change-updated";

/** The SSE event the server sends when a change is archived: its card leaves (change-documents-and-history D10). */
export const CHANGE_ARCHIVED = "change-archived";

/**
 * The SSE event the server sends when the viewer joins a Project, leaves it or changes role there (projects D3, §11;
 * spec change-visibility, "Membership changes take effect live").
 */
export const PROJECTS_CHANGED = "projects-changed";

/** A `projects-changed` event's data: the Project, and whether the viewer is in it now. */
export interface ProjectsChange {
  projectId: string;
  member: boolean;
}

/**
 * The SSE event the server sends to a Project's members when one of its tags is added, renamed or deleted (tags D6;
 * server: Dashboard/DashboardEndpoints).
 */
export const PROJECT_TAGS_CHANGED = "project-tags-changed";

/** A `project-tags-changed` event's data: every tag the Project has now, by key then value. */
export interface ProjectTagsChange {
  projectId: string;
  tags: ProjectTagRef[];
}

/** The SSE event the server sends when a session on a change has a new state (agent-interaction D4). */
export const SESSION_UPDATED = "session-updated";

/** The SSE event the server sends when a session leaves a change: it ended, expired or moved to another change. */
export const SESSION_ENDED = "session-ended";

/** A `session-updated` event's data. */
export interface SessionUpdate {
  changeId: string;
  session: SessionView;
}

/** A `session-ended` event's data. */
export interface SessionEnd {
  changeId: string;
  sessionId: string;
}

/** The SSE event the server sends to a member's own streams when their away switch changes (agent-interaction D8). */
export const AWAY_CHANGED = "away-changed";

/** The SSE event the server sends when an answer from the dashboard is recorded on a change (agent-interaction D8). */
export const ANSWER_RECORDED = "answer-recorded";

/**
 * The SSE event the server sends when the stream's queue dropped events (code review F8): the page loads its state
 * again, as on a reconnect. It has no data.
 */
export const RESYNC = "resync";

/** The parts of the browser's EventSource this module uses, so tests can supply a fake. */
export interface EventSourceLike {
  readonly readyState: number;
  onopen: ((event: Event) => void) | null;
  onerror: ((event: Event) => void) | null;
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void;
  close(): void;
}

export type EventSourceFactory = (url: string) => EventSourceLike;

export interface LiveOptions {
  createEventSource?: EventSourceFactory | undefined;
  /** Delays before reopening after the browser gives up on a connection; the last one repeats. */
  retryDelaysMs?: readonly number[] | undefined;
  setTimer?: ((callback: () => void, ms: number) => unknown) | undefined;
  /**
   * Asked after a stream error, to tell "signed out" from "offline" (design D18): resolves false when the session
   * has ended. The stream then stops for good, with no reconnect. A check that fails counts as still signed in.
   */
  checkSignedIn?: (() => Promise<boolean>) | undefined;
  /** The viewer's away switch changed, from this or another dashboard, or `panopticon away`. */
  onAway?: ((state: AwayState) => void) | undefined;
  /**
   * The stream may have missed events (a reconnect after the first open, or `resync`): state outside the cards, such
   * as the away switch, loads again. The cards reload themselves.
   */
  onCatchUp?: (() => void) | undefined;
}

/** EventSource.CLOSED: the browser won't retry by itself. */
const CLOSED = 2;

/**
 * Keeps the dashboard live (spec live-dashboard, "Live updates", and design D9). On every (re)connect it refreshes
 * all cards from `GET /api/changes`, then applies `change-updated` events in place, ignoring stale ones. While
 * disconnected it shows the offline state. The browser retries a dropped stream by itself; when it gives up (the
 * stream is closed) this reopens it after a back-off. Returns a function that stops it.
 */
export function connectLive(app: App, options: LiveOptions = {}): () => void {
  const create = options.createEventSource ?? ((url) => new EventSource(url));
  const delays = options.retryDelaysMs ?? [1000, 2000, 5000, 10000];
  const setTimer = options.setTimer ?? ((callback, ms) => setTimeout(callback, ms));

  let source: EventSourceLike | null = null;
  let failures = 0;
  let stopped = false;
  let opened = false;

  function open(): void {
    const current = create("/api/events");
    source = current;

    current.onopen = () => {
      // Events can be missed while disconnected, so start from the server's full state.
      if (opened) {
        options.onCatchUp?.();
      }

      opened = true;
      app.refresh().then(
        () => {
          failures = 0;
          app.setStatus("live");
        },
        () => app.setStatus("offline"),
      );
    };

    current.addEventListener(CHANGE_UPDATED, (event) => {
      const card = parseCard(event.data);
      if (card) {
        app.upsert(card);
      }
    });

    current.addEventListener(CHANGE_ARCHIVED, (event) => {
      const changeId = parseArchivedId(event.data);
      if (changeId) {
        app.remove(changeId);
      }
    });

    current.addEventListener(PROJECTS_CHANGED, (event) => {
      const change = parseProjectsChange(event.data);
      if (!change) {
        return;
      }

      if (!change.member) {
        app.leaveProject(change.projectId);
        return;
      }

      // Joined (or a new role): the Project's active cards come from the server. If that load fails, the dashboard is
      // behind, so it reconnects, and the reconnect's refresh catches up.
      app.refresh().catch(() => {
        app.setStatus("offline");
        current.close();
        retry();
      });
    });

    // Sessions move a card in place, never its order (spec live-dashboard, Sessions don't reorder cards).
    current.addEventListener(SESSION_UPDATED, (event) => {
      const update = parseSessionUpdate(event.data);
      if (update) {
        app.updateSession(update.changeId, update.session);
      }
    });

    current.addEventListener(SESSION_ENDED, (event) => {
      const end = parseSessionEnd(event.data);
      if (end) {
        app.endSession(end.changeId, end.sessionId);
      }
    });

    current.addEventListener(PROJECT_TAGS_CHANGED, (event) => {
      const change = parseProjectTagsChange(event.data);
      if (change) {
        app.retagProject(change);
      }
    });

    // The server's queue for this stream overflowed: catch up as on a reconnect. If the load fails, reconnect.
    current.addEventListener(RESYNC, () => {
      options.onCatchUp?.();
      app.refresh().catch(() => {
        app.setStatus("offline");
        current.close();
        retry();
      });
    });

    current.addEventListener(AWAY_CHANGED, (event) => {
      const state = parseAwayState(event.data);
      if (state) {
        options.onAway?.(state);
      }
    });

    current.onerror = () => {
      app.setStatus("offline");
      if (options.checkSignedIn) {
        // The browser doesn't say why a stream failed: a 401 (the session ended) looks like a dropped connection.
        void options.checkSignedIn().then(
          (signedIn) => (signedIn ? retry() : stopFor(current)),
          () => retry(),
        );
        return;
      }

      retry();
    };

    function stopFor(failed: EventSourceLike): void {
      stopped = true;
      failed.close();
    }

    function retry(): void {
      if (current.readyState === CLOSED && !stopped) {
        current.close();
        const delay = delays[Math.min(failures, delays.length - 1)] ?? 1000;
        failures++;
        setTimer(() => {
          if (!stopped) {
            open();
          }
        }, delay);
      }
    }
  }

  open();

  return () => {
    stopped = true;
    source?.close();
  };
}

/** The `changeId` of a `change-archived` event's data, or null when it isn't one. */
export function parseArchivedId(data: string): string | null {
  try {
    const value = JSON.parse(data) as { changeId?: unknown } | null;
    return typeof value?.changeId === "string" ? value.changeId : null;
  } catch {
    return null;
  }
}

const SESSION_STATES: readonly string[] = [
  "working",
  "waiting-on-question",
  "waiting-on-permission",
  "waiting-on-user",
];

/** A `session-updated` event's data, or null when it isn't one. */
export function parseSessionUpdate(data: string): SessionUpdate | null {
  try {
    const value = JSON.parse(data) as { changeId?: unknown; session?: unknown } | null;
    const session = value?.session as Partial<SessionView> | null | undefined;
    return typeof value?.changeId === "string" &&
      typeof session?.id === "string" &&
      typeof session.memberId === "string" &&
      typeof session.memberName === "string" &&
      typeof session.machine === "string" &&
      typeof session.since === "string" &&
      typeof session.lastEventAt === "string" &&
      typeof session.state === "string" &&
      SESSION_STATES.includes(session.state)
      ? { changeId: value.changeId, session: session as SessionView }
      : null;
  } catch {
    return null;
  }
}

/** A `session-ended` event's data, or null when it isn't one. */
export function parseSessionEnd(data: string): SessionEnd | null {
  try {
    const value = JSON.parse(data) as Partial<SessionEnd> | null;
    return typeof value?.changeId === "string" && typeof value.sessionId === "string"
      ? { changeId: value.changeId, sessionId: value.sessionId }
      : null;
  } catch {
    return null;
  }
}

/** An `away-changed` event's data (or `/api/me/away`'s answer), or null when it isn't one. */
export function parseAwayState(data: string): AwayState | null {
  try {
    const value = JSON.parse(data) as Partial<AwayState> | null;
    return typeof value?.away === "boolean"
      ? { away: value.away, since: typeof value.since === "string" ? value.since : null }
      : null;
  } catch {
    return null;
  }
}

/** An `answer-recorded` event's data, or null when it isn't one. */
export function parseAnswerEntry(data: string): AnswerEntry | null {
  try {
    const value = JSON.parse(data) as Partial<AnswerEntry> | null;
    return typeof value?.id === "string" &&
      typeof value.changeId === "string" &&
      typeof value.sessionMember === "string" &&
      typeof value.answeredBy === "string" &&
      typeof value.answeredAt === "string" &&
      (value.kind === "question" || value.kind === "permission")
      ? (value as AnswerEntry)
      : null;
  } catch {
    return null;
  }
}

/** A `projects-changed` event's data, or null when it isn't one. */
export function parseProjectsChange(data: string): ProjectsChange | null {
  try {
    const value = JSON.parse(data) as Partial<ProjectsChange> | null;
    return typeof value?.projectId === "string" && typeof value.member === "boolean"
      ? { projectId: value.projectId, member: value.member }
      : null;
  } catch {
    return null;
  }
}

/** A `project-tags-changed` event's data, or null when it isn't one. */
export function parseProjectTagsChange(data: string): ProjectTagsChange | null {
  try {
    const value = JSON.parse(data) as { projectId?: unknown; tags?: unknown } | null;
    if (typeof value?.projectId !== "string" || !Array.isArray(value.tags)) {
      return null;
    }

    const tags: ProjectTagRef[] = [];
    for (const tag of value.tags as unknown[]) {
      const { id, key, value: tagValue } = (tag ?? {}) as Partial<Record<string, unknown>>;
      if (typeof id !== "string" || typeof key !== "string") {
        return null;
      }

      tags.push({ id, key, value: typeof tagValue === "string" ? tagValue : null });
    }

    return { projectId: value.projectId, tags };
  } catch {
    return null;
  }
}

/**
 * `card` with its Project's tags as `change` has them (tags D6): renamed by id, in the Project's order, with the ids
 * that are gone dropped. The same card when it isn't in that Project, or nothing changed.
 */
export function retagCard(card: ChangeCard, change: ProjectTagsChange): ChangeCard {
  if (card.project?.id.toLowerCase() !== change.projectId.toLowerCase()) {
    return card;
  }

  const on = new Set(card.projectTags.map((tag) => tag.id.toLowerCase()));
  const projectTags = change.tags.filter((tag) => on.has(tag.id.toLowerCase()));
  const same =
    projectTags.length === card.projectTags.length &&
    projectTags.every((tag, index) => {
      const was = card.projectTags[index];
      return was?.id === tag.id && was.key === tag.key && was.value === tag.value;
    });
  return same ? card : { ...card, projectTags };
}

/** A `change-updated` event's card, or null when the data isn't one. */
export function parseCard(data: string): ChangeCard | null {
  try {
    const card = JSON.parse(data) as Partial<ChangeCard> | null;
    return card && typeof card.changeId === "string" && typeof card.lastReportedAt === "string"
      ? (card as ChangeCard)
      : null;
  } catch {
    return null;
  }
}
