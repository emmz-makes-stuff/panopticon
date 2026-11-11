import { describe, expect, it } from "vitest";
import type { Api } from "./api";
import { awaySwitch } from "./away";
import type { AwayState } from "./types";

// Second review #8: a reload of the away switch never puts an older state back.

function deferredApi() {
  const gets: ((state: AwayState) => void)[] = [];
  const api: Api = {
    get: <T>() =>
      new Promise<T>((resolve) => {
        gets.push((state) => resolve(state as T));
      }),
    send: <T>() => Promise.resolve(undefined as T),
  };
  return { api, gets };
}

const shown = (button: HTMLButtonElement) => button.getAttribute("aria-pressed");

describe("awaySwitch.load", () => {
  it("shows the server's state", async () => {
    const { api, gets } = deferredApi();
    const away = awaySwitch(api, () => undefined);

    const loading = away.load();
    gets[0]?.({ away: true });
    await loading;

    expect(shown(away.element)).toBe("true");
  });

  it("ignores an answer that lands after a newer away-changed", async () => {
    const { api, gets } = deferredApi();
    const away = awaySwitch(api, () => undefined);

    const loading = away.load();
    away.set({ away: true });
    gets[0]?.({ away: false });
    await loading;

    expect(shown(away.element)).toBe("true");
  });

  it("ignores an older load that lands after a newer one", async () => {
    const { api, gets } = deferredApi();
    const away = awaySwitch(api, () => undefined);

    const older = away.load();
    const newer = away.load();
    gets[1]?.({ away: true });
    await newer;
    gets[0]?.({ away: false });
    await older;

    expect(shown(away.element)).toBe("true");
  });
});
