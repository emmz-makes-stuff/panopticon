import { ApiError } from "../api";
import { mountApp, PRODUCT_NAME } from "../app";
import { parseFilters, toQuery } from "../filters";
import { connectLive } from "../live";
import type { View } from "../router";
import type { Env } from "../shell";

/**
 * The tab's title with `count` held prompts waiting on the viewer (spec live-dashboard, Needs-you count in the tab
 * title): "(2) PANOPTICON", or just the name with none.
 */
export function titleWith(count: number, base = PRODUCT_NAME): string {
  return count > 0 ? `(${count}) ${base}` : base;
}

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
      viewerId: env.member()?.id,
      // Worked out from the live state on every change (D10): the tab title's count, and any new notification.
      onHeld: (held) => {
        document.title = titleWith(held.length);
        env.notifier.held(held);
      },
      filters: parseFilters(params),
      onFiltersChange: (filters) => {
        const query = toQuery(filters);
        navigate(query ? `/?${query}` : "/");
      },
    });
    // The cards show at once with the default thresholds; the deployment's apply when they arrive (review fix 10.3).
    let left = false;
    void env.quietSettings().then((settings) => {
      if (!left) {
        app.setQuiet(settings);
      }
    });
    const stopLive = connectLive(app, {
      createEventSource: env.createEventSource,
      onAway: (state) => env.awayChanged(state),
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
        left = true;
        stopLive();
        clearInterval(timer);
        document.title = titleWith(0);
        env.statusSlot.replaceChildren();
      },
      update: (next) => app.setFilters(parseFilters(next)),
    };
  };
}
