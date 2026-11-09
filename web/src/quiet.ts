/**
 * Quiet cards (spec live-dashboard, "Quiet cards"; filters-and-idle-cards D2): a card is active until its change's last
 * report is older than the idle threshold, idle until it's older than the stale one, then stale. The state is a pure
 * function of the time, so an open dashboard moves it on by re-reading it against the clock.
 */

export type QuietState = "active" | "idle" | "stale";

/** `GET /api/settings`: the deployment's thresholds, in seconds (server: DashboardSettings). */
export interface QuietSettings {
  idleAfterSeconds: number;
  staleAfterSeconds: number;
}

/** The server's defaults (30 minutes and 4 hours), used when the settings can't be loaded. */
export const DEFAULT_QUIET: QuietSettings = {
  idleAfterSeconds: 30 * 60,
  staleAfterSeconds: 4 * 3600,
};

/** A change's state at `now`. A report time in the future (clock skew) is active. */
export function quietState(lastReportedAt: string, now: Date, settings: QuietSettings): QuietState {
  const ageSeconds = (now.getTime() - Date.parse(lastReportedAt)) / 1000;
  if (ageSeconds > settings.staleAfterSeconds) {
    return "stale";
  }

  return ageSeconds > settings.idleAfterSeconds ? "idle" : "active";
}

/** Whether `value` is usable settings: both thresholds positive numbers, and stale after idle. */
export function isQuietSettings(value: unknown): value is QuietSettings {
  const settings = value as Partial<QuietSettings> | null;
  return (
    typeof settings?.idleAfterSeconds === "number" &&
    typeof settings.staleAfterSeconds === "number" &&
    settings.idleAfterSeconds > 0 &&
    settings.staleAfterSeconds > settings.idleAfterSeconds
  );
}
