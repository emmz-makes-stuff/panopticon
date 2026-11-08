import { ApiError } from "../api";
import { mountApp } from "../app";
import { connectLive } from "../live";
import type { View } from "../router";
import type { Env } from "../shell";

/** How often each card's "last reported" time is re-read against the clock. */
export const TIMES_REFRESH_MS = 30_000;

/**
 * `/`: the cards, kept live (spec live-dashboard). The stream opens only now, once the session is known. When it
 * fails, a session check tells "signed out" (stop, and the shell shows sign-in) from "offline" (reconnect).
 */
export function cardsView(env: Env): View {
  return async ({ root }) => {
    const app = await mountApp(root, { api: env.api, now: env.now, statusSlot: env.statusSlot });
    const stopLive = connectLive(app, {
      createEventSource: env.createEventSource,
      checkSignedIn: () =>
        env.api.get("/api/session").then(
          () => true,
          (error: unknown) =>
            error instanceof ApiError && error.status === 401 ? false : Promise.reject(error),
        ),
    });
    const timer = setInterval(() => app.updateTimes(), TIMES_REFRESH_MS);
    return () => {
      stopLive();
      clearInterval(timer);
      env.statusSlot.replaceChildren();
    };
  };
}
