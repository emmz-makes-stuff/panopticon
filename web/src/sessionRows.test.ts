import { describe, expect, it } from "vitest";
import { mountApp } from "./app";
import {
  CHANGE_UPDATED,
  connectLive,
  parseSessionEnd,
  parseSessionUpdate,
  SESSION_ENDED,
  SESSION_UPDATED,
} from "./live";
import { elapsed, heldPrompts, needs, sessionText } from "./sessionRows";
import { card, FakeEventSource, fakeFetch, NOW, q, settle } from "./testing";
import type { ChangeCard, SessionView } from "./types";

// Spec live-dashboard: "Sessions on a card", "Sessions don't reorder cards", and the modified "Quiet cards" and
// "Quiet state moves on by itself"; agent-interaction D10. Rows read with commas (Emmz, 2026-11-11).

const EMMZ = "0193f7a0-0000-7000-8000-00000000e001";
const SAM = "0193f7a0-0000-7000-8000-00000000e002";

const ago = (minutes: number) => new Date(NOW.getTime() - minutes * 60_000).toISOString();

function session(overrides: Partial<SessionView> = {}): SessionView {
  return {
    id: "s1",
    memberId: EMMZ,
    memberName: "Emmz",
    machine: "mbp",
    state: "working",
    activity: "Run make gates",
    since: ago(0),
    lastEventAt: ago(0),
    ...overrides,
  };
}

async function start(initial: ChangeCard[], viewerId = EMMZ) {
  const root = document.createElement("main");
  let now = NOW;
  const app = await mountApp(root, { fetch: fakeFetch(initial), now: () => now, viewerId });
  connectLive(app, {
    createEventSource: (url) => new FakeEventSource(url),
    setTimer: () => undefined,
  });
  FakeEventSource.latest().open();
  await settle();

  const cardFor = (changeId: string) => q(root, `article.card[data-change-id="${changeId}"]`);
  const rows = (changeId: string) =>
    [...cardFor(changeId).querySelectorAll<HTMLElement>(".card-session")].map(
      (row) => row.textContent,
    );
  const update = (changeId: string, next: SessionView) =>
    FakeEventSource.latest().emit(SESSION_UPDATED, JSON.stringify({ changeId, session: next }));
  const end = (changeId: string, sessionId: string) =>
    FakeEventSource.latest().emit(SESSION_ENDED, JSON.stringify({ changeId, sessionId }));
  const order = () =>
    [...root.querySelectorAll<HTMLElement>("article.card")].map((c) => c.dataset["changeId"]);
  const tick = (to: Date) => {
    now = to;
    app.updateTimes();
  };
  return { root, app, cardFor, rows, update, end, order, tick };
}

describe("session rows", () => {
  it("Running gates: a session's activity shows on its card within the event, with no reload", async () => {
    const { rows, update } = await start([card({ changeId: "tags", change: "tags" })]);

    update("tags", session());

    expect(rows("tags")).toEqual(["Emmz on mbp: Run make gates, just now"]);
  });

  it("shows the activity, with who and where in the time's tooltip (and to screen readers)", async () => {
    const { cardFor, update } = await start([card({ changeId: "tags", change: "tags" })]);

    update("tags", session());

    const row = q(cardFor("tags"), ".card-session");
    expect(q(row, ".card-session-who").classList.contains("visually-hidden")).toBe(true);
    expect(q(row, ".card-session-what").textContent).toBe("Run make gates");
    const time = q<HTMLTimeElement>(row, ".card-session-time");
    expect(time.textContent).toBe("just now");
    expect(time.title).toBe(`Emmz on mbp, ${new Date(NOW).toLocaleString()}`);
  });

  it("Waiting since lunch: a turn that ended 2 hours ago says how long it has waited", async () => {
    const { rows } = await start([
      card({
        changeId: "tags",
        sessions: [session({ state: "waiting-on-user", activity: null, since: ago(120) })],
      }),
    ]);

    expect(rows("tags")).toEqual(["Emmz on mbp: waiting for you, 2h"]);
  });

  it("names the member it waits for to anyone else", async () => {
    const { rows } = await start(
      [
        card({
          changeId: "tags",
          sessions: [session({ state: "waiting-on-user", activity: null })],
        }),
      ],
      SAM,
    );

    expect(rows("tags")).toEqual(["Emmz on mbp: waiting for Emmz, just now"]);
  });

  it("says what a prompt waits on, a permission's tool included, in text", () => {
    expect(sessionText(session({ state: "waiting-on-question" }), EMMZ)).toBe(
      "waiting on a question",
    );
    expect(
      sessionText(
        session({ state: "waiting-on-permission", prompt: { kind: "permission", tool: "Bash" } }),
        SAM,
      ),
    ).toBe("waiting on permission for Bash");
    expect(
      sessionText(session({ state: "waiting-on-visit", prompt: { kind: "visit" } }), SAM),
    ).toBe("waiting on a visit request");
    expect(sessionText(session({ activity: null }), EMMZ)).toBe("working");
  });

  it("A visit request needs you, or names its member to anyone else: 'Needs Emmz: visit a page'", () => {
    const visiting = session({ state: "waiting-on-visit", prompt: { kind: "visit" } });

    expect(needs(visiting, EMMZ)).toBe("Needs you");
    expect(needs(visiting, SAM)).toBe("Needs Emmz: visit a page");
  });

  it("counts a held visit request as a held prompt of kind visit", () => {
    const held = heldPrompts([
      card({
        changeId: "tags",
        change: "tags",
        sessions: [
          session({
            state: "waiting-on-visit",
            prompt: { kind: "visit" },
            promptDetail: {
              id: "p1",
              held: true,
              askedAt: "2026-11-13T12:00:00Z",
              visit: { url: "https://example.com/", reason: "Look" },
            },
          }),
        ],
      }),
    ]);

    expect(held.map((p) => [p.promptId, p.kind])).toEqual([["p1", "visit"]]);
  });

  it("lists each session in the server's order, and keeps names as text", async () => {
    const { rows, cardFor } = await start([
      card({
        changeId: "tags",
        sessions: [
          session({ id: "s1", memberName: "<b>Emmz</b>" }),
          session({
            id: "s2",
            memberId: SAM,
            memberName: "Sam",
            machine: "vm-2",
            activity: "Editing tasks.md",
            since: ago(4),
          }),
        ],
      }),
    ]);

    expect(rows("tags")).toEqual([
      "<b>Emmz</b> on mbp: Run make gates, just now",
      "Sam on vm-2: Editing tasks.md, 4m",
    ]);
    expect(cardFor("tags").querySelector("b")).toBeNull();
    expect(cardFor("tags").querySelector(".card-sessions")?.getAttribute("aria-label")).toBe(
      "Sessions",
    );
  });

  it("updates in place: the card and an unchanged row keep their elements", async () => {
    const { rows, update, cardFor } = await start([
      card({
        changeId: "tags",
        sessions: [session({ id: "s1" }), session({ id: "s2", machine: "vm-2" })],
      }),
    ]);
    const article = cardFor("tags");
    const second = article.querySelector('[data-session-id="s2"]');

    update("tags", session({ id: "s1", activity: "Editing tasks.md" }));

    expect(cardFor("tags")).toBe(article);
    expect(article.querySelector('[data-session-id="s2"]')).toBe(second);
    expect(rows("tags")).toEqual([
      "Emmz on mbp: Editing tasks.md, just now",
      "Emmz on vm-2: Run make gates, just now",
    ]);
    expect(article.classList.contains("updated")).toBe(false);
  });

  it("drops a session's row when it ends, and the list when none are left", async () => {
    const { rows, end, cardFor } = await start([card({ changeId: "tags", sessions: [session()] })]);

    end("tags", "s1");

    expect(rows("tags")).toEqual([]);
    expect(cardFor("tags").querySelector(".card-sessions")).toBeNull();
  });

  it("a newer report still listing an ended session doesn't bring its row back (code review F1)", async () => {
    const { rows, end } = await start([
      card({ changeId: "tags", lastReportedAt: ago(5), sessions: [session()] }),
    ]);

    end("tags", "s1");
    FakeEventSource.latest().emit(
      CHANGE_UPDATED,
      JSON.stringify(card({ changeId: "tags", lastReportedAt: ago(0), sessions: [session()] })),
    );

    expect(rows("tags")).toEqual([]);
  });

  it("a report for a card new to the dashboard brings its sessions", async () => {
    const { rows } = await start([]);

    FakeEventSource.latest().emit(
      CHANGE_UPDATED,
      JSON.stringify(card({ changeId: "tags", sessions: [session()] })),
    );

    expect(rows("tags")).toEqual(["Emmz on mbp: Run make gates, just now"]);
  });

  it("moves the elapsed time on with the quiet clock", async () => {
    const { rows, tick } = await start([
      card({ changeId: "tags", sessions: [session({ since: ago(0) })] }),
    ]);

    tick(new Date(NOW.getTime() + 5 * 60_000));

    expect(rows("tags")).toEqual(["Emmz on mbp: Run make gates, 5m"]);
  });

  it("Busy older change: a session event never reorders the cards", async () => {
    const { update, order } = await start([
      card({ changeId: "newer", lastReportedAt: ago(1) }),
      card({ changeId: "tags", lastReportedAt: ago(10) }),
    ]);

    update("tags", session());

    expect(order()).toEqual(["newer", "tags"]);
  });

  it("ignores events for a change that isn't shown, or that aren't sessions", async () => {
    const { rows, update, root } = await start([card({ changeId: "tags" })]);

    update("elsewhere", session());
    FakeEventSource.latest().emit(SESSION_UPDATED, "not json");

    expect(rows("tags")).toEqual([]);
    expect(root.querySelectorAll(".card-session")).toHaveLength(0);
  });

  it("a refresh catches the sessions up without rebuilding the card", async () => {
    const shared = card({ changeId: "tags", sessions: [session()] });
    const root = document.createElement("main");
    const fetch = fakeFetch([shared]);
    const app = await mountApp(root, { fetch, now: () => NOW, viewerId: EMMZ });
    const article = root.querySelector("article.card");

    fetch.cards = [{ ...shared, sessions: [session({ activity: "Reading design.md" })] }];
    await app.refresh();

    expect(root.querySelector("article.card")).toBe(article);
    expect(root.querySelector(".card-session")?.textContent).toBe(
      "Emmz on mbp: Reading design.md, just now",
    );
  });
});

describe("quiet cards with sessions", () => {
  const quietState = (article: HTMLElement) => article.dataset["quiet"];

  it("Running gates for an hour: reported an hour ago, a tool a minute ago, the card is active", async () => {
    const { cardFor } = await start([
      card({
        changeId: "tags",
        lastReportedAt: ago(60),
        lastActiveAt: ago(1),
        sessions: [session({ lastEventAt: ago(1) })],
      }),
    ]);

    expect(quietState(cardFor("tags"))).toBe("active");
  });

  it("Session wakes an idle card: active as soon as the event arrives", async () => {
    const { cardFor, update } = await start([card({ changeId: "tags", lastReportedAt: ago(60) })]);
    expect(quietState(cardFor("tags"))).toBe("idle");

    update("tags", session({ lastEventAt: NOW.toISOString() }));

    expect(quietState(cardFor("tags"))).toBe("active");
  });

  it("Quiet for a morning: a stale card's last seen is its last activity", async () => {
    const { cardFor } = await start([
      card({ changeId: "tags", lastReportedAt: ago(9 * 60), lastActiveAt: ago(5 * 60) }),
    ]);

    expect(quietState(cardFor("tags"))).toBe("stale");
    expect(cardFor("tags").querySelector(".card-time")?.textContent).toBe("last seen 5h ago");
  });

  it("Goes idle while watching: a quiet session lets the card go idle as the clock passes", async () => {
    const { cardFor, tick } = await start([
      card({
        changeId: "tags",
        lastReportedAt: ago(60),
        lastActiveAt: ago(0),
        sessions: [session({ lastEventAt: ago(0) })],
      }),
    ]);

    tick(new Date(NOW.getTime() + 31 * 60_000));

    expect(quietState(cardFor("tags"))).toBe("idle");
  });
});

describe("session event data", () => {
  it("reads a session-updated event, refusing an unknown state or a missing field", () => {
    const good = { changeId: "tags", session: session() };
    expect(parseSessionUpdate(JSON.stringify(good))?.session.id).toBe("s1");
    expect(
      parseSessionUpdate(JSON.stringify({ ...good, session: { ...good.session, state: "ended" } })),
    ).toBeNull();
    expect(
      parseSessionUpdate(JSON.stringify({ changeId: "tags", session: { id: "s1" } })),
    ).toBeNull();
    expect(parseSessionUpdate("[")).toBeNull();
  });

  it("reads a session-ended event", () => {
    expect(parseSessionEnd(JSON.stringify({ changeId: "tags", sessionId: "s1" }))).toEqual({
      changeId: "tags",
      sessionId: "s1",
    });
    expect(parseSessionEnd(JSON.stringify({ changeId: "tags" }))).toBeNull();
  });

  it("says elapsed time without 'ago'", () => {
    expect(elapsed(ago(0), NOW)).toBe("just now");
    expect(elapsed(ago(59), NOW)).toBe("59m");
    expect(elapsed(ago(120), NOW)).toBe("2h");
    expect(elapsed(ago(3 * 24 * 60), NOW)).toBe("3d");
  });
});
