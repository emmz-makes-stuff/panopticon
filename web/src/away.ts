import { ApiError, type Api } from "./api";
import { parseAwayState } from "./live";
import type { AwayState } from "./types";

/** The dashboard's side of the away switch (`/api/me/away`; agent-interaction D3, D8). */
export const AWAY_PATH = "/api/me/away";

export interface AwaySwitch {
  /** The toggle button, for the header. */
  element: HTMLButtonElement;
  /** Shows `state`: from the server's answer, or an `away-changed` event. */
  set(state: AwayState): void;
  /**
   * Asks the server for the member's current state. A failure leaves the switch as it is, and says so. An answer that
   * lands after a newer state was set, or after a later load began, is ignored.
   */
  load(): Promise<void>;
}

/**
 * The away switch in the header (spec live-dashboard, Away switch in the header; D10): a real toggle button, with
 * `aria-pressed` and its state in text, so it isn't colour alone. Turning it on or off works as `panopticon away` does,
 * and the server's answer (or a later `away-changed`) is what it shows. `onError` says what went wrong in text, or
 * clears it with "".
 */
export function awaySwitch(api: Api, onError: (text: string) => void): AwaySwitch {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "away-switch";
  // Its name stays "Away"; aria-pressed says whether it's on (a toggle's name doesn't change with its state).
  button.setAttribute("aria-label", "Away");
  const label = document.createElement("span");
  label.className = "away-switch-label";
  label.textContent = "Away";
  const state = document.createElement("span");
  state.className = "away-switch-state";
  state.setAttribute("aria-hidden", "true");
  button.append(label, " ", state);

  let away = false;
  // Second review #8: bumped by every `set`, so a load that started before a newer state (a toggle, or `away-changed`)
  // doesn't put the older one back; and only the newest load applies.
  let version = 0;
  let loads = 0;
  function set(next: AwayState): void {
    version++;
    away = next.away;
    button.setAttribute("aria-pressed", String(away));
    button.dataset["away"] = String(away);
    state.textContent = away ? "on" : "off";
    button.title = away
      ? "Your sessions' prompts wait for the dashboard. Select to answer them in the terminal again."
      : "Select to have your sessions' prompts wait for the dashboard.";
  }

  set({ away: false });

  button.addEventListener("click", () => {
    onError("");
    button.disabled = true;
    api
      .send<unknown>("PUT", AWAY_PATH, { away: !away })
      .then(
        (answer) => {
          const next = parseAwayState(JSON.stringify(answer ?? null));
          if (next) {
            set(next);
          }
        },
        (error: unknown) => {
          if (!(error instanceof ApiError && error.status === 401)) {
            onError(
              away
                ? "Couldn't turn away mode off. Try again."
                : "Couldn't turn away mode on. Try again.",
            );
          }
        },
      )
      .finally(() => {
        button.disabled = false;
      });
  });

  return {
    element: button,
    set,
    async load() {
      const mine = ++loads;
      const since = version;
      try {
        const next = parseAwayState(JSON.stringify((await api.get<unknown>(AWAY_PATH)) ?? null));
        if (next && mine === loads && since === version) {
          set(next);
        }
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 401)) {
          onError("Couldn't load your away switch.");
        }
      }
    },
  };
}
