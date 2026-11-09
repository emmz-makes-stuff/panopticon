/**
 * The filter model shared by the dashboard and the history (spec change-filters; filters-and-idle-cards D4, D5): what
 * the address's query means, how to write it back, and whether a change matches.
 */
import type { QuietState } from "./quiet";
import type { CardAgent, CardRepo, ChangeCard } from "./types";

export interface OwnerRef {
  host: string;
  /** May contain '/' (a GitLab subgroup); matched whole. */
  owner: string;
}

/** One value per dimension, each optional. A change matches only when it matches every one given. */
export interface Filters {
  repo?: CardRepo;
  owner?: OwnerRef;
  /** A member id. */
  member?: string;
  machine?: string;
  /** An agent type: the MCP client name, e.g. `claude-code`. */
  client?: string;
  /** Dashboard only; the history ignores it. */
  status?: QuietState;
}

export type FilterKey = keyof Filters;

/** The query keys, in the order the app writes them, so the same filters always give the same address. */
export const FILTER_KEYS: readonly FilterKey[] = [
  "repo",
  "owner",
  "member",
  "machine",
  "client",
  "status",
];

const STATUSES: readonly QuietState[] = ["active", "idle", "stale"];

export interface ParsedFilters {
  filters: Filters;
  /** Keys whose value didn't parse, and so were dropped (the filter bar says so). */
  dropped: FilterKey[];
}

/** The filters in `params`. Unknown keys are ignored, blank values mean no filter, and malformed ones are dropped. */
export function parseFilters(params: URLSearchParams): ParsedFilters {
  const filters: Filters = {};
  const dropped: FilterKey[] = [];
  const value = (key: FilterKey) => params.get(key)?.trim() || undefined;

  const repo = value("repo");
  if (repo !== undefined) {
    const parsed = parseRepo(repo);
    if (parsed) {
      filters.repo = parsed;
    } else {
      dropped.push("repo");
    }
  }

  const owner = value("owner");
  if (owner !== undefined) {
    const parsed = parseOwner(owner);
    if (parsed) {
      filters.owner = parsed;
    } else {
      dropped.push("owner");
    }
  }

  const member = value("member");
  if (member !== undefined) {
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(member)) {
      filters.member = member.toLowerCase();
    } else {
      dropped.push("member");
    }
  }

  const machine = value("machine");
  if (machine !== undefined) {
    filters.machine = machine;
  }

  const client = value("client");
  if (client !== undefined) {
    filters.client = client;
  }

  const status = value("status");
  if (status !== undefined) {
    if ((STATUSES as readonly string[]).includes(status)) {
      filters.status = status as QuietState;
    } else {
      dropped.push("status");
    }
  }

  return { filters, dropped };
}

/** `host/owner…/name`: the first segment is the host, the last the name, and the rest the owner (as the server reads it). */
export function parseRepo(value: string): CardRepo | undefined {
  const segments = segmentsOf(value);
  return segments && segments.length >= 3
    ? {
        host: segments[0] ?? "",
        owner: segments.slice(1, -1).join("/"),
        name: segments.at(-1) ?? "",
      }
    : undefined;
}

/** `host/owner…`: the first segment is the host, and the rest the owner. */
export function parseOwner(value: string): OwnerRef | undefined {
  const segments = segmentsOf(value);
  return segments && segments.length >= 2
    ? { host: segments[0] ?? "", owner: segments.slice(1).join("/") }
    : undefined;
}

function segmentsOf(value: string): string[] | undefined {
  const segments = value
    .trim()
    .split("/")
    .map((segment) => segment.trim());
  return segments.some((segment) => segment === "") ? undefined : segments;
}

export const repoValue = (repo: CardRepo) => `${repo.host}/${repo.owner}/${repo.name}`;
export const ownerValue = (owner: OwnerRef) => `${owner.host}/${owner.owner}`;

/** The query value of one filter, or undefined when it isn't set. */
export function filterValue(filters: Filters, key: FilterKey): string | undefined {
  switch (key) {
    case "repo":
      return filters.repo && repoValue(filters.repo);
    case "owner":
      return filters.owner && ownerValue(filters.owner);
    default:
      return filters[key];
  }
}

/**
 * The filters as a query string, without the '?': keys in `FILTER_KEYS` order, encoded by `URLSearchParams`, and only
 * those in `keys` (the history leaves out `status`). Empty when no filter is set.
 */
export function toQuery(filters: Filters, keys: readonly FilterKey[] = FILTER_KEYS): string {
  const params = new URLSearchParams();
  for (const key of FILTER_KEYS) {
    const value = keys.includes(key) ? filterValue(filters, key) : undefined;
    if (value !== undefined) {
      params.set(key, value);
    }
  }

  return params.toString();
}

export const hasFilters = (filters: Filters, keys: readonly FilterKey[] = FILTER_KEYS) =>
  keys.some((key) => filters[key] !== undefined);

/** What a change has that filters look at: a card, or a history entry. */
export interface Filterable {
  repo: CardRepo;
  agents: CardAgent[];
}

const same = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

/**
 * Whether a change matches every filter. Repos, owners, machines and agent types compare without regard to case, as
 * identity does; a person matches when any agent reported with that member's token. `status` compares with `state`,
 * and is skipped when no state is given.
 */
export function matches(change: Filterable, filters: Filters, state?: QuietState): boolean {
  const { repo, owner, member, machine, client, status } = filters;
  if (
    repo &&
    !(
      same(change.repo.host, repo.host) &&
      same(change.repo.owner, repo.owner) &&
      same(change.repo.name, repo.name)
    )
  ) {
    return false;
  }

  if (owner && !(same(change.repo.host, owner.host) && same(change.repo.owner, owner.owner))) {
    return false;
  }

  if (member && !change.agents.some((agent) => agent.memberId?.toLowerCase() === member)) {
    return false;
  }

  if (machine && !change.agents.some((agent) => same(agent.machine, machine))) {
    return false;
  }

  if (client && !change.agents.some((agent) => same(agent.client, client))) {
    return false;
  }

  return !(status && state !== undefined && state !== status);
}

/** The values a page offers to filter by (the shape of `GET /api/history/filters`, server: HistoryChoices). */
export interface FilterChoices {
  repos: CardRepo[];
  owners: OwnerRef[];
  members: { id: string; displayName: string }[];
  machines: string[];
  clients: string[];
}

/**
 * Case-insensitive ordinal order, as the server sorts the history's choices (.NET `OrdinalIgnoreCase`, which compares
 * upper-cased code units), so the two pages list the same values in the same order (review fix 10.7).
 */
export const byLabel = (a: string, b: string): number => {
  const [x, y] = [a.toUpperCase(), b.toUpperCase()];
  return x < y ? -1 : x > y ? 1 : 0;
};

/** Field by field, as the server does: host, then owner, then name. */
const byFields = (...pairs: [string, string][]): number => {
  for (const [a, b] of pairs) {
    const order = byLabel(a, b);
    if (order !== 0) {
      return order;
    }
  }

  return 0;
};

/**
 * The dashboard's choices (design D5): the distinct repos, owners, members, machines and agent types on `cards`, each
 * sorted case-insensitively. Values that differ only in case are one choice. A member is named as on their most recent
 * card, and an agent with no member adds no person.
 */
export function filterChoices(cards: readonly ChangeCard[]): FilterChoices {
  const newestFirst = [...cards].sort(
    (a, b) => Date.parse(b.lastReportedAt) - Date.parse(a.lastReportedAt),
  );
  const repos = new Map<string, CardRepo>();
  const owners = new Map<string, OwnerRef>();
  const members = new Map<string, string>();
  const machines = new Map<string, string>();
  const clients = new Map<string, string>();
  for (const card of newestFirst) {
    const repoKey = repoValue(card.repo).toLowerCase();
    if (!repos.has(repoKey)) {
      repos.set(repoKey, card.repo);
    }

    const ownerKey = ownerValue(card.repo).toLowerCase();
    if (!owners.has(ownerKey)) {
      owners.set(ownerKey, { host: card.repo.host, owner: card.repo.owner });
    }

    for (const agent of card.agents) {
      const id = agent.memberId?.toLowerCase();
      if (id && agent.member && !members.has(id)) {
        members.set(id, agent.member);
      }

      if (!machines.has(agent.machine.toLowerCase())) {
        machines.set(agent.machine.toLowerCase(), agent.machine);
      }

      if (!clients.has(agent.client.toLowerCase())) {
        clients.set(agent.client.toLowerCase(), agent.client);
      }
    }
  }

  return {
    repos: [...repos.values()].sort((a, b) =>
      byFields([a.host, b.host], [a.owner, b.owner], [a.name, b.name]),
    ),
    owners: [...owners.values()].sort((a, b) => byFields([a.host, b.host], [a.owner, b.owner])),
    members: [...members.entries()]
      .map(([id, displayName]) => ({ id, displayName }))
      .sort((a, b) => byLabel(a.displayName, b.displayName) || a.id.localeCompare(b.id)),
    machines: [...machines.values()].sort(byLabel),
    clients: [...clients.values()].sort(byLabel),
  };
}

/** One option of a filter select: its query value, its label, and whether no change on the page has it. */
export interface FilterOption {
  value: string;
  label: string;
  missing?: boolean;
}

export type FilterOptions = Record<Exclude<FilterKey, "status">, FilterOption[]>;

/**
 * The options for each select. A chosen value that isn't among the choices (no change has it any more, or a shared link
 * names it) stays offered, marked missing, so the bar can still show it and clear it.
 */
export function filterOptions(choices: FilterChoices, filters: Filters): FilterOptions {
  const options: FilterOptions = {
    repo: choices.repos.map((repo) => ({ value: repoValue(repo), label: repoValue(repo) })),
    owner: choices.owners.map((owner) => ({ value: ownerValue(owner), label: ownerValue(owner) })),
    member: choices.members.map((m) => ({ value: m.id.toLowerCase(), label: m.displayName })),
    machine: choices.machines.map((machine) => ({ value: machine, label: machine })),
    client: choices.clients.map((client) => ({ value: client, label: client })),
  };

  for (const key of Object.keys(options) as (keyof FilterOptions)[]) {
    const chosen = filterValue(filters, key);
    if (chosen !== undefined && !options[key].some((option) => same(option.value, chosen))) {
      const label = key === "member" ? "Unknown member" : chosen;
      options[key].unshift({ value: chosen, label: `${label} (no changes)`, missing: true });
    }
  }

  return options;
}

/** The option that stands for the chosen value: matched without regard to case, as filtering is. */
export function chosenOption(options: readonly FilterOption[], value: string | undefined) {
  return value === undefined ? undefined : options.find((option) => same(option.value, value));
}
