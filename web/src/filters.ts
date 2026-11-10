/**
 * The filter model shared by the dashboard and the history (spec change-filters; filters-and-idle-cards D4, D5): what
 * the address's query means, how to write it back, and whether a change matches.
 */
import type { QuietState } from "./quiet";
import { parseTag } from "./tagSyntax";
import type {
  CardAgent,
  CardProject,
  CardRepo,
  ChangeCard,
  PersonalTagRef,
  ProjectTagRef,
} from "./types";

export interface OwnerRef {
  host: string;
  /** May contain '/' (a GitLab subgroup); matched whole. */
  owner: string;
}

/** One value per dimension, each optional. A change matches only when it matches every one given. */
export interface Filters {
  /** A Project id (lower case), or `PERSONAL` for the viewer's personal work (projects D9). */
  project?: string;
  repo?: CardRepo;
  owner?: OwnerRef;
  /** A member id. */
  member?: string;
  machine?: string;
  /** An agent type: the MCP client name, e.g. `claude-code`. */
  client?: string;
  /** Dashboard only; the history ignores it. */
  status?: QuietState;
  /**
   * The one filter with many values (tags D7): a change matches only when it has every one. In canonical order, with
   * no repeats, and never empty (no tags chosen is undefined).
   */
  tags?: TagFilter[];
}

/**
 * One chosen tag (tags D7; server: Changes/TagFilter), keys and values lower-cased, as tags match: a Project tag by id,
 * a Project's key alone with any value, or the viewer's own personal tag (with no value, their key alone).
 */
export type TagFilter =
  | { kind: "tag"; id: string }
  | { kind: "key"; projectId: string; key: string }
  | { kind: "personal"; key: string; value: string | null };

export type FilterKey = keyof Filters;

/** The filters with one value each: a select in the filter bar. */
export type SingleFilterKey = Exclude<FilterKey, "tags">;

/**
 * The filter keys, in the order the app writes them, so the same filters always give the same address. `tags` is
 * written last, as repeated `tag` parameters.
 */
export const FILTER_KEYS: readonly FilterKey[] = [
  "project",
  "repo",
  "owner",
  "member",
  "machine",
  "client",
  "status",
  "tags",
];

/** The query parameter for the Tags filter, repeated once per tag. */
export const TAG_PARAM = "tag";

/** At most this many tags are read from an address; the rest are dropped (the server refuses more, tags D7). */
export const MAX_FILTER_TAGS = 20;

const STATUSES: readonly QuietState[] = ["active", "idle", "stale"];

/** The `project` value for personal work, as the server reads it (HistoryFilter.PersonalValue). */
export const PERSONAL = "personal";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export interface ParsedFilters {
  filters: Filters;
  /** Keys whose value didn't parse, and so were dropped (the filter bar says so). */
  dropped: FilterKey[];
  /** Whether the address had more than `MAX_FILTER_TAGS` tags, so only the first were kept. */
  tagsOverLimit?: boolean;
}

/** The filters in `params`. Unknown keys are ignored, blank values mean no filter, and malformed ones are dropped. */
export function parseFilters(params: URLSearchParams): ParsedFilters {
  const filters: Filters = {};
  const dropped: FilterKey[] = [];
  const value = (key: SingleFilterKey) => params.get(key)?.trim() || undefined;

  const project = value("project");
  if (project !== undefined) {
    if (project.toLowerCase() === PERSONAL || GUID.test(project)) {
      filters.project = project.toLowerCase();
    } else {
      dropped.push("project");
    }
  }

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
    if (GUID.test(member)) {
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

  // Blank values are ignored and a repeat counts once, as on the server; a malformed one, or one past the limit, is
  // dropped, keeping the rest.
  const tags = new Map<string, TagFilter>();
  let droppedTag = false;
  let overLimit = false;
  for (const raw of params.getAll(TAG_PARAM)) {
    if (raw.trim() === "") {
      continue;
    }

    const tag = parseTagFilter(raw);
    if (!tag) {
      droppedTag = true;
      continue;
    }

    const canonical = tagFilterValue(tag);
    if (!tags.has(canonical)) {
      if (tags.size === MAX_FILTER_TAGS) {
        overLimit = true;
        continue;
      }

      tags.set(canonical, tag);
    }
  }

  if (tags.size > 0) {
    filters.tags = sortTags([...tags.values()]);
  }

  if (droppedTag) {
    dropped.push("tags");
  }

  return overLimit ? { filters, dropped, tagsOverLimit: true } : { filters, dropped };
}

/**
 * One `tag` value (tags D7), read as the server reads it: `t:<tag id>`, `k:<project id>:<key>`, or `p:<key>` or
 * `p:<key>:<value>`, where the value is everything after the key's ':' (so it can contain ':'). Null when malformed.
 */
export function parseTagFilter(value: string): TagFilter | null {
  const trimmed = value.trim();
  if (trimmed.length < 2 || trimmed[1] !== ":") {
    return null;
  }

  const rest = trimmed.slice(2);
  switch (trimmed[0]) {
    case "t":
      return GUID.test(rest) ? { kind: "tag", id: rest.toLowerCase() } : null;
    case "k": {
      const colon = rest.indexOf(":");
      const projectId = colon < 0 ? "" : rest.slice(0, colon);
      const parsed = colon < 0 ? null : parseTag(rest.slice(colon + 1));
      return GUID.test(projectId) && parsed && "tag" in parsed && parsed.tag.value === null
        ? { kind: "key", projectId: projectId.toLowerCase(), key: parsed.tag.key.toLowerCase() }
        : null;
    }
    case "p": {
      const parsed = parseTag(rest);
      return "tag" in parsed
        ? {
            kind: "personal",
            key: parsed.tag.key.toLowerCase(),
            value: parsed.tag.value?.toLowerCase() ?? null,
          }
        : null;
    }
    default:
      return null;
  }
}

/** A chosen tag's `tag` value, as the server writes it (TagFilter.ToString). */
export function tagFilterValue(tag: TagFilter): string {
  switch (tag.kind) {
    case "tag":
      return `t:${tag.id}`;
    case "key":
      return `k:${tag.projectId}:${tag.key}`;
    case "personal":
      return tag.value === null ? `p:${tag.key}` : `p:${tag.key}:${tag.value}`;
  }
}

/** Ordinal order, as the server compares tag keys and values (`StringComparer.Ordinal`). */
export const ordinal = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0);

/** Tags in canonical order: by `tag` value, ordinally, so the same tags always give the same address. */
export function sortTags(tags: readonly TagFilter[]): TagFilter[] {
  return [...tags]
    .map((tag) => ({ tag, value: tagFilterValue(tag) }))
    .sort((a, b) => ordinal(a.value, b.value))
    .map(({ tag }) => tag);
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

/** The query value of one single-valued filter, or undefined when it isn't set. */
export function filterValue(filters: Filters, key: SingleFilterKey): string | undefined {
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
 * those in `keys` (the history leaves out `status`). The tags go last, one `tag` each, in canonical order. Empty when
 * no filter is set.
 */
export function toQuery(filters: Filters, keys: readonly FilterKey[] = FILTER_KEYS): string {
  const params = new URLSearchParams();
  for (const key of FILTER_KEYS) {
    if (!keys.includes(key)) {
      continue;
    }

    if (key === "tags") {
      for (const tag of sortTags(filters.tags ?? [])) {
        params.append(TAG_PARAM, tagFilterValue(tag));
      }

      continue;
    }

    const value = filterValue(filters, key);
    if (value !== undefined) {
      params.set(key, value);
    }
  }

  return params.toString();
}

export const hasFilters = (filters: Filters, keys: readonly FilterKey[] = FILTER_KEYS) =>
  keys.some((key) =>
    key === "tags" ? (filters.tags?.length ?? 0) > 0 : filters[key] !== undefined,
  );

/** What a change has that filters look at: a card, or a history entry. */
export interface Filterable {
  repo: CardRepo;
  agents: CardAgent[];
  project: CardProject | null;
  personal: boolean;
  /** Its Project's tags on it. */
  projectTags: ProjectTagRef[];
  /** The viewer's own personal tags on it. */
  personalTags: PersonalTagRef[];
}

/** Whether a change has one chosen tag: compared without regard to case, as tags are (tags D1). */
export function hasTag(change: Filterable, tag: TagFilter): boolean {
  switch (tag.kind) {
    case "tag":
      return change.projectTags.some((t) => t.id.toLowerCase() === tag.id);
    case "key":
      // A card's Project tags are all its Project's, so the key matches only there.
      return (
        change.project?.id.toLowerCase() === tag.projectId &&
        change.projectTags.some((t) => t.key.toLowerCase() === tag.key)
      );
    case "personal":
      return change.personalTags.some(
        (t) =>
          t.key.toLowerCase() === tag.key &&
          (tag.value === null || (t.value ?? "").toLowerCase() === tag.value),
      );
  }
}

const same = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

/**
 * Whether a change matches every filter. Repos, owners, machines and agent types compare without regard to case, as
 * identity does; a person matches when any agent reported with that member's token. `status` compares with `state`,
 * and is skipped when no state is given.
 */
export function matches(change: Filterable, filters: Filters, state?: QuietState): boolean {
  const { project, repo, owner, member, machine, client, status, tags } = filters;
  // tags D7: every chosen tag. A deleted tag, or one no change has, matches nothing.
  if (tags && !tags.every((tag) => hasTag(change, tag))) {
    return false;
  }

  // A Project matches by id, so a rename doesn't break it; one the viewer isn't in matches nothing, as no card has it.
  if (
    project &&
    !(project === PERSONAL
      ? change.personal && change.project === null
      : change.project?.id.toLowerCase() === project)
  ) {
    return false;
  }

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
  /** The viewer's Projects with a change on the page, by code. */
  projects: CardProject[];
  /** Whether the viewer has a personal change on the page, so "Personal" is offered. */
  personal: boolean;
  /**
   * The tags on the page's changes, as the viewer sees them (tags D7): their own personal tags, then each Project's by
   * code, each group by key then value, with a key that has values also offered alone, above them.
   */
  tags: TagChoice[];
}

/**
 * A Tags filter choice (server: Changes/TagChoice): `filter` is its `tag` value, and `project` is null for the viewer's
 * own personal tag. `value` is null for a bare tag or a key offered alone.
 */
export interface TagChoice {
  filter: string;
  project: CardProject | null;
  key: string;
  value: string | null;
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
  const projects = new Map<string, CardProject>();
  let personal = false;
  for (const card of newestFirst) {
    // Named as on its most recent card, so a rename shows at once.
    if (card.project && !projects.has(card.project.id.toLowerCase())) {
      projects.set(card.project.id.toLowerCase(), card.project);
    }

    personal ||= card.personal && card.project === null;
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
    projects: [...projects.values()].sort((a, b) => byLabel(a.code, b.code)),
    personal,
    tags: tagChoices(newestFirst),
  };
}

interface RankedChoice {
  keyKey: string;
  /** The key alone first, then a bare tag, then the valued ones. */
  rank: number;
  valueKey: string;
  choice: TagChoice;
}

const byRank = (a: RankedChoice, b: RankedChoice) =>
  ordinal(a.keyKey, b.keyKey) || a.rank - b.rank || ordinal(a.valueKey, b.valueKey);

/**
 * The dashboard's Tags choices, in the order the server gives the history's (server: ChangePages, tags D7). A Project
 * tag is spelled as on its most recent card, so a rename shows at once. A personal tag reported in several casings is
 * one choice, spelled as the one sorting last ordinally, as on the server. For a personal tag, the key alone and the
 * bare tag are one filter (`p:<key>`), so one choice.
 */
function tagChoices(newestFirst: readonly ChangeCard[]): TagChoice[] {
  const personal = new Map<string, RankedChoice>();
  const addPersonal = (
    keyKey: string,
    key: string,
    valueKey: string | null,
    value: string | null,
  ) => {
    const filter = tagFilterValue({ kind: "personal", key: keyKey, value: valueKey });
    const current = personal.get(filter);
    const spelled = {
      key: current && ordinal(current.choice.key, key) > 0 ? current.choice.key : key,
      value,
    };
    if (
      current &&
      value !== null &&
      current.choice.value !== null &&
      ordinal(current.choice.value, value) > 0
    ) {
      spelled.value = current.choice.value;
    }

    personal.set(filter, {
      keyKey,
      rank: valueKey === null ? 0 : 1,
      valueKey: valueKey ?? "",
      choice: { filter, project: null, key: spelled.key, value: spelled.value },
    });
  };

  const projects = new Map<string, { project: CardProject; choices: Map<string, RankedChoice> }>();
  for (const card of newestFirst) {
    for (const tag of card.personalTags) {
      const keyKey = tag.key.toLowerCase();
      addPersonal(keyKey, tag.key, tag.value?.toLowerCase() ?? null, tag.value);
      if (tag.value !== null) {
        addPersonal(keyKey, tag.key, null, null);
      }
    }

    if (!card.project) {
      continue;
    }

    const projectId = card.project.id.toLowerCase();
    const group = projects.get(projectId) ?? { project: card.project, choices: new Map() };
    projects.set(projectId, group);
    for (const tag of card.projectTags) {
      const keyKey = tag.key.toLowerCase();
      const filter = tagFilterValue({ kind: "tag", id: tag.id.toLowerCase() });
      if (!group.choices.has(filter)) {
        group.choices.set(filter, {
          keyKey,
          rank: tag.value === null ? 1 : 2,
          valueKey: tag.value?.toLowerCase() ?? "",
          choice: { filter, project: group.project, key: tag.key, value: tag.value },
        });
      }

      // The key alone is spelled as the valued tag whose key sorts last ordinally, as on the server.
      const keyFilter = tagFilterValue({ kind: "key", projectId, key: keyKey });
      const keyAlone = group.choices.get(keyFilter);
      if (tag.value !== null && (!keyAlone || ordinal(keyAlone.choice.key, tag.key) < 0)) {
        group.choices.set(keyFilter, {
          keyKey,
          rank: 0,
          valueKey: "",
          choice: { filter: keyFilter, project: group.project, key: tag.key, value: null },
        });
      }
    }
  }

  return [
    ...[...personal.values()].sort(byRank),
    ...[...projects.values()]
      .sort((a, b) => byLabel(a.project.code, b.project.code))
      .flatMap((group) => [...group.choices.values()].sort(byRank)),
  ].map((ranked) => ranked.choice);
}

/** One option of a filter select: its query value, its label, and whether no change on the page has it. */
export interface FilterOption {
  value: string;
  label: string;
  missing?: boolean;
}

/** The selects' keys: every single-valued filter but status, whose options are fixed. */
export type SelectKey = Exclude<SingleFilterKey, "status">;

const SELECT_KEYS: readonly SelectKey[] = [
  "project",
  "repo",
  "owner",
  "member",
  "machine",
  "client",
];

/** Each select's options, and the Tags filter's choices (the bar names a chosen tag that isn't among them). */
export type FilterOptions = Record<SelectKey, FilterOption[]> & { tags: TagChoice[] };

/**
 * The options for each select. A chosen value that isn't among the choices (no change has it any more, or a shared link
 * names it) stays offered, marked missing, so the bar can still show it and clear it.
 */
export function filterOptions(choices: FilterChoices, filters: Filters): FilterOptions {
  const options: Record<SelectKey, FilterOption[]> = {
    // Offered by code and name, chosen by id (projects D9); "Personal" last, when the viewer has personal work here.
    project: [
      ...choices.projects.map((p) => ({
        value: p.id.toLowerCase(),
        label: `${p.code}: ${p.name}`,
      })),
      ...(choices.personal ? [{ value: PERSONAL, label: "Personal" }] : []),
    ],
    repo: choices.repos.map((repo) => ({ value: repoValue(repo), label: repoValue(repo) })),
    owner: choices.owners.map((owner) => ({ value: ownerValue(owner), label: ownerValue(owner) })),
    member: choices.members.map((m) => ({ value: m.id.toLowerCase(), label: m.displayName })),
    machine: choices.machines.map((machine) => ({ value: machine, label: machine })),
    client: choices.clients.map((client) => ({ value: client, label: client })),
  };

  for (const key of SELECT_KEYS) {
    const chosen = filterValue(filters, key);
    if (chosen !== undefined && !options[key].some((option) => same(option.value, chosen))) {
      const label =
        key === "member"
          ? "Unknown member"
          : key === "project"
            ? chosen === PERSONAL
              ? "Personal"
              : "Unknown Project"
            : chosen;
      options[key].unshift({ value: chosen, label: `${label} (no changes)`, missing: true });
    }
  }

  return { ...options, tags: choices.tags };
}

/** The option that stands for the chosen value: matched without regard to case, as filtering is. */
export function chosenOption(options: readonly FilterOption[], value: string | undefined) {
  return value === undefined ? undefined : options.find((option) => same(option.value, value));
}
