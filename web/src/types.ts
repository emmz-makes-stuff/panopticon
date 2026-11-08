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
