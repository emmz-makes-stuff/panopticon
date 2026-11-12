// The JSON shape of `GET /api/changes` and of each `change-updated` SSE event's data
// (server: Panopticon.Server.Dashboard.ChangeCard, serialised camelCase).

/** Done over total. A total of 0 is the "empty" state: no tasks in tasks.md. */
export interface Progress {
  done: number;
  total: number;
}

export interface CardRepo {
  host: string;
  owner: string;
  name: string;
}

export interface CardAgent {
  client: string;
  machine: string;
  /** ISO 8601. */
  lastReportedAt: string;
  /** The display name of the member whose token reported, or null (the Development bootstrap token, or older rows). */
  member?: string | null;
  /** That member's id, which the person filter matches on (filters-and-idle-cards D6); null with no member. */
  memberId?: string | null;
}

/** A change's Project, as its card, page and history row show it (projects D9). */
export interface CardProject {
  id: string;
  code: string;
  name: string;
}

export interface CardSection {
  number: number;
  title: string;
  progress: Progress;
}

/** A Project's tag (tags D3): chosen by id, so a rename keeps links and filters. `value` is null for a bare key. */
export interface ProjectTagRef {
  id: string;
  key: string;
  value: string | null;
}

/** One of the viewer's own personal tags (tags D5): only they ever receive it. */
export interface PersonalTagRef {
  key: string;
  value: string | null;
}

export interface ChangeCard {
  changeId: string;
  repo: CardRepo;
  change: string;
  agents: CardAgent[];
  currentSection: CardSection | null;
  overall: Progress;
  /** ISO 8601. */
  lastReportedAt: string;
  /** Its Project, or null for personal work and a bootstrap reporter's. */
  project: CardProject | null;
  /** Its owner's personal work (shown as "Personal"); false for a bootstrap change, which shows no label. */
  personal: boolean;
  /** Its Project's tags on it, by key then value (tags D6). */
  projectTags: ProjectTagRef[];
  /** The viewer's own personal tags on it, by key then value (tags D6). */
  personalTags: PersonalTagRef[];
  /** The change's Claude Code sessions right now, oldest first (agent-interaction D4). Absent from older servers. */
  sessions?: SessionView[];
  /** ISO 8601: its last report or its sessions' last event, whichever is later (spec live-dashboard, Quiet cards). */
  lastActiveAt?: string;
}

// Sessions and their prompts (server: Panopticon.Server.Presence, agent-interaction D4, D5).

/** What a session is doing (spec agent-presence, Session states). Ended sessions leave the card. */
export type SessionState =
  "working" | "waiting-on-question" | "waiting-on-permission" | "waiting-on-user";

/** What a session waits on, as everyone who can see it sees it. */
export interface SessionPrompt {
  kind: "question" | "permission";
  /** A permission prompt's tool, e.g. "Bash". */
  tool?: string | null;
}

/** One option of a held question. */
export interface PromptOption {
  label: string;
  description?: string | null;
  /**
   * The option's preview, already rendered by the server as documents are (spec remote-answers, Safe rendering): it's
   * inserted as is.
   */
  previewHtml?: string | null;
}

/** One question of a held prompt. */
export interface PromptQuestion {
  question: string;
  header: string;
  options: PromptOption[];
  multiSelect: boolean;
}

/** The prompt itself: only for a viewer who may answer it (D5). Its presence means "Needs you". */
export interface PromptDetail {
  id: string;
  /** False: it's the terminal's to answer. */
  held: boolean;
  askedAt: string;
  questions?: PromptQuestion[] | null;
  /** A held permission prompt's input, cut to 2 KB, once it has arrived. */
  preview?: string | null;
}

/** A member's away switch (`GET`/`PUT /api/me/away`, and the `away-changed` event; agent-interaction D8). */
export interface AwayState {
  away: boolean;
  /** ISO 8601: since when, while away. */
  since?: string | null;
}

/** One question and the answer given, as an answer's record keeps it. */
export interface AnsweredQuestion {
  question: string;
  answer: string;
}

/**
 * An answer given from the dashboard (spec remote-answers, Answers are attributed), as the change page lists it and
 * `answer-recorded` sends it. `questions` comes only to a viewer who could have answered (D5).
 */
export interface AnswerEntry {
  id: string;
  changeId: string;
  sessionMember: string;
  answeredBy: string;
  /** ISO 8601. */
  answeredAt: string;
  kind: "question" | "permission";
  tool?: string | null;
  /** A permission prompt's: "allow" or "deny". */
  outcome?: "allow" | "deny" | null;
  questions?: AnsweredQuestion[] | null;
}

/** One Claude Code session on a change (spec live-dashboard, Sessions on a card). */
export interface SessionView {
  id: string;
  memberId: string;
  memberName: string;
  machine: string;
  state: SessionState;
  /** The one-line activity, while working. */
  activity?: string | null;
  /** ISO 8601: when the current state and activity began. */
  since: string;
  lastEventAt: string;
  prompt?: SessionPrompt | null;
  promptDetail?: PromptDetail | null;
}

// The change page and history (server: Panopticon.Server.Changes, change-documents-and-history D9).

/** What a document is, for ordering and labelling (server: ChangePages.Describe). */
export type DocumentKind = "proposal" | "design" | "tasks" | "spec" | "devlog" | "meta" | "other";

/** One of a change's latest synced documents, in the server's reading order. */
export interface DocumentEntry {
  /** Relative to the change's folder, with '/' separators. */
  path: string;
  kind: DocumentKind;
  /** The capability for a spec; the slug and time for a devlog note; the path otherwise. */
  title: string;
  sizeBytes: number;
}

/** `GET /api/changes/{id}`: the change as its card shows it, whether it's archived, and its documents. */
export interface ChangePage {
  card: ChangeCard;
  /** ISO 8601, or null while the change is active. */
  archivedAt: string | null;
  archiveFolder: string | null;
  documents: DocumentEntry[];
  /** Answers given from the dashboard, newest first (spec remote-answers, Answers are attributed). */
  answers?: AnswerEntry[];
}

/** `GET /api/changes/{id}/documents/{path}`: HTML the server has already made safe (D8). */
export interface RenderedDocument {
  path: string;
  html: string;
}

/** One archived change in the history. */
export interface HistoryEntry {
  changeId: string;
  repo: CardRepo;
  change: string;
  /** ISO 8601. */
  archivedAt: string;
  /** Its archive folder; null for a personal copy retired by linking (projects D4). */
  archiveFolder: string | null;
  agents: CardAgent[];
  project: CardProject | null;
  personal: boolean;
  /** For a retired copy, the Project its work went to: shown as "Linked to PAN". */
  linkedTo: CardProject | null;
  projectTags: ProjectTagRef[];
  personalTags: PersonalTagRef[];
}

/** `GET /api/history`: newest archive first; `nextBefore` is the next page's cursor, or null on the last page. */
export interface HistoryPage {
  changes: HistoryEntry[];
  nextBefore: string | null;
}

// Metrics (server: Panopticon.Server.Metrics, change-metrics D4). Durations are whole seconds, wall-clock.

/** One measured section of a change: from its start to when every task stayed ticked. */
export interface SectionMetric {
  number: number;
  title: string;
  /** ISO 8601. */
  startedAt: string;
  /** ISO 8601, or null while in progress. */
  finishedAt: string | null;
  /** Its time, or its time so far while in progress. */
  durationSeconds: number;
  inProgress: boolean;
}

/** `GET /api/changes/{id}/metrics`. */
export interface ChangeMetrics {
  /** ISO 8601. */
  firstReportedAt: string;
  /** ISO 8601, or null while active. */
  archivedAt: string | null;
  /** Its time to archive, or its time so far while active. */
  durationSeconds: number;
  active: boolean;
  /** Measured sections, in number order. */
  sections: SectionMetric[];
}

/** One Monday-start UTC week of archives. */
export interface WeekMetric {
  /** ISO 8601: Monday 00:00 UTC. */
  weekStart: string;
  changes: number;
  /** Null when no change was archived that week. */
  medianArchiveSeconds: number | null;
}

/** One of the slowest finished sections, with the change it belongs to. */
export interface SlowSection {
  changeId: string;
  change: string;
  repo: CardRepo;
  number: number;
  title: string;
  durationSeconds: number;
}

/** `GET /api/metrics`: over the archived changes the filters match. */
export interface TeamMetrics {
  changes: number;
  /** Null when no change matches. */
  medianArchiveSeconds: number | null;
  /** Null when the matching changes have no finished section. */
  medianSectionSeconds: number | null;
  /** The last 12 weeks, oldest first. */
  weeks: WeekMetric[];
  /** The 10 slowest, slowest first. */
  slowestSections: SlowSection[];
}
