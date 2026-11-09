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
