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
