import type { CardAgent, Progress } from "./types";

/** Whole-number percentage, or null for the empty state (no tasks). */
export function percent(progress: Progress): number | null {
  return progress.total === 0 ? null : Math.round((progress.done / progress.total) * 100);
}

/** The bar's visible count, e.g. "2/5 (40%)" (spec live-dashboard, "Bars match tasks.md"). */
export function countText(progress: Progress): string {
  return `${progress.done}/${progress.total} (${percent(progress) ?? 0}%)`;
}

/** An agent as the card names it: `client@machine`. */
export function agentName(agent: CardAgent): string {
  return `${agent.client}@${agent.machine}`;
}

/** "just now", "14m ago", "3h ago", "2d ago". A time in the future (clock skew) reads "just now". */
export function relativeTime(iso: string, now: Date): string {
  const seconds = Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) {
    return "just now";
  }

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours}h ago`;
  }

  return `${Math.floor(hours / 24)}d ago`;
}

/** A calendar date such as "8 Nov 2026" (UTC, so it reads the same wherever the browser is). */
export function calendarDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
    timeZone: "UTC",
  });
}

/** Whole days, hours and minutes in a duration; anything negative counts as none. */
function parts(seconds: number): { days: number; hours: number; minutes: number } {
  const minutesTotal = Math.floor(Math.max(0, seconds) / 60);
  return {
    days: Math.floor(minutesTotal / 1440),
    hours: Math.floor(minutesTotal / 60) % 24,
    minutes: minutesTotal % 60,
  };
}

/**
 * A duration as the metrics show it (change-metrics D5): "<1m", "45m", "3h 30m", "5d 6h". The two largest units, the
 * smaller left out when it's zero ("3h", "5d").
 */
export function formatDuration(seconds: number): string {
  const { days, hours, minutes } = parts(seconds);
  if (days > 0) {
    return hours > 0 ? `${days}d ${hours}h` : `${days}d`;
  }

  if (hours > 0) {
    return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
  }

  return minutes > 0 ? `${minutes}m` : "<1m";
}

/** The same duration in words, for screen readers and tooltips: "5 days 6 hours", "1 minute", "less than a minute". */
export function formatDurationLong(seconds: number): string {
  const { days, hours, minutes } = parts(seconds);
  const unit = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;
  if (days > 0) {
    return hours > 0 ? `${unit(days, "day")} ${unit(hours, "hour")}` : unit(days, "day");
  }

  if (hours > 0) {
    return minutes > 0 ? `${unit(hours, "hour")} ${unit(minutes, "minute")}` : unit(hours, "hour");
  }

  return minutes > 0 ? unit(minutes, "minute") : "less than a minute";
}
