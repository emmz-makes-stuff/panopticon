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

export interface CardSection {
  number: number;
  title: string;
  progress: Progress;
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
  archiveFolder: string;
  agents: CardAgent[];
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
