import { ApiError } from "../api";
import { mountApp } from "../app";
import { parseFilters, toQuery } from "../filters";
import { connectLive } from "../live";
import type { View } from "../router";
import type { Env } from "../shell";

/** How often each card's "last reported" time is re-read against the clock. */
export const TIMES_REFRESH_MS = 30_000;

/**
 * `/`: the cards, kept live (spec live-dashboard), narrowed by the filters in the address (spec change-filters). The
 * stream opens only now, once the session is known. When it fails, a session check tells "signed out" (stop, and the
 * shell shows sign-in) from "offline" (reconnect).
 *
 * A filter choice writes the address (`/?repo=…`, design D4). The router sees the same path and calls `update` rather
 * than rebuilding the view, so there's no refetch and no reconnect, while Back and Forward step through the choices.
 */
export function cardsView(env: Env): View {
  return async ({ root, params, navigate }) => {
    const app = await mountApp(root, {
      api: env.api,
      now: env.now,
      statusSlot: env.statusSlot,
      quiet: await env.quietSettings(),
      filters: parseFilters(params),
      onFiltersChange: (filters) => {
        const query = toQuery(filters);
        navigate(query ? `/?${query}` : "/");
      },
    });
    const stopLive = connectLive(app, {
      createEventSource: env.createEventSource,
      checkSignedIn: () =>
        env.api.get("/api/session").then(
          () => true,
          (error: unknown) =>
            error instanceof ApiError && error.status === 401 ? false : Promise.reject(error),
        ),
    });
    // Times and quiet states move on with the clock (design D2): a card goes idle within this long of the threshold.
    const timer = setInterval(() => app.updateTimes(), TIMES_REFRESH_MS);
    return {
      teardown: () => {
        stopLive();
        clearInterval(timer);
        env.statusSlot.replaceChildren();
      },
      update: (next) => app.setFilters(parseFilters(next)),
    };
  };
}
