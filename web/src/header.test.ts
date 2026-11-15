import { beforeEach, describe, expect, it } from "vitest";
import { AWAY_CHANGED, SESSION_UPDATED } from "./live";
import { NOTIFICATIONS_KEY, type NotificationHost } from "./notifications";
import { signedIn, signedOut, startTestShell } from "./testShell";
import { card, FakeEventSource, MEMBER, q, until } from "./testing";
import type { SessionView } from "./types";
import { titleWith } from "./views/cards";

// Spec live-dashboard: "Away switch in the header", "Needs-you count in the tab title" and "Browser notifications";
// agent-interaction D10.

const NOW = "2026-11-08T12:00:00Z";

function heldSession(
  id: string,
  promptId: string,
  overrides: Partial<SessionView> = {},
): SessionView {
  return {
    id,
    memberId: MEMBER.id,
    memberName: MEMBER.displayName,
    machine: "mbp",
    state: "waiting-on-question",
    since: NOW,
    lastEventAt: NOW,
    prompt: { kind: "question" },
    promptDetail: {
      id: promptId,
      held: true,
      askedAt: NOW,
      questions: [
        { question: "Which?", header: "Pick", options: [{ label: "A" }], multiSelect: false },
      ],
    },
    ...overrides,
  };
}

/** A notification host that records what it's asked, granting or denying permission as told. */
function fakeNotifications(answer: NotificationPermission = "granted") {
  let permission: NotificationPermission = "default";
  const shown: { title: string; options: NotificationOptions; click: () => void }[] = [];
  let asked = 0;
  const host: NotificationHost = {
    permission: () => permission,
    requestPermission: () => {
      asked++;
      permission = answer;
      return Promise.resolve(answer);
    },
    show: (title, options, click) => shown.push({ title, options, click }),
  };
  return { host, shown, asked: () => asked, grant: () => (permission = "granted") };
}

const awaySwitch = (root: HTMLElement) => q<HTMLButtonElement>(root, ".away-switch");
const accountError = (root: HTMLElement) => root.querySelector(".account-error")?.textContent ?? "";

beforeEach(() => {
  FakeEventSource.instances.length = 0;
  document.title = titleWith(0);
});

describe("Away switch in the header", () => {
  it("Turning away on: it reads Away, pressed, and the server is told", async () => {
    const server = signedIn().on("PUT", "/api/me/away", {
      status: 200,
      body: { away: true, since: NOW },
    });
    const { root } = await startTestShell("/", server);
    await until(() => server.to("GET", "/api/me/away").length === 1);

    const button = awaySwitch(root);
    expect(button.getAttribute("aria-pressed")).toBe("false");
    expect(button.textContent).toBe("Away off");
    expect(button.getAttribute("aria-label")).toBe("Away");

    button.click();
    await until(() => button.getAttribute("aria-pressed") === "true");

    expect(server.to("PUT", "/api/me/away")[0]?.body).toEqual({ away: true });
    expect(button.textContent).toBe("Away on");
  });

  it("starts as the server says", async () => {
    const server = signedIn().on("GET", "/api/me/away", {
      status: 200,
      body: { away: true, since: NOW },
    });
    const { root } = await startTestShell("/", server);

    await until(() => awaySwitch(root).getAttribute("aria-pressed") === "true");
    expect(awaySwitch(root).textContent).toBe("Away on");
  });

  it("follows away-changed live, from panopticon away or another tab", async () => {
    const { root } = await startTestShell("/", signedIn());
    await until(() => FakeEventSource.instances.length === 1);
    FakeEventSource.latest().open();

    FakeEventSource.latest().emit(AWAY_CHANGED, JSON.stringify({ away: true, since: NOW }));

    expect(awaySwitch(root).getAttribute("aria-pressed")).toBe("true");
  });

  it("says so in text when the switch can't be changed, and stays as it was", async () => {
    const server = signedIn().on("PUT", "/api/me/away", { status: 500 });
    const { root } = await startTestShell("/", server);

    awaySwitch(root).click();
    await until(() => accountError(root) !== "");

    expect(accountError(root)).toBe("Couldn't turn away mode on. Try again.");
    expect(awaySwitch(root).getAttribute("aria-pressed")).toBe("false");
  });
});

describe("Needs-you count in the tab title", () => {
  it("Two prompts: (2) in the title, then down as they're answered, and none with none", async () => {
    const server = signedIn().on("GET", "/api/changes", {
      status: 200,
      body: [
        card({ changeId: "tags", change: "tags", sessions: [heldSession("s1", "p1")] }),
        card({ changeId: "web", change: "web", sessions: [heldSession("s2", "p2")] }),
      ],
    });
    await startTestShell("/", server);
    await until(() => document.title !== "PANOPTICON");

    expect(document.title).toBe("(2) PANOPTICON");

    FakeEventSource.latest().open();
    await until(() => server.to("GET", "/api/changes").length === 2);
    FakeEventSource.latest().emit(
      SESSION_UPDATED,
      JSON.stringify({
        changeId: "tags",
        session: heldSession("s1", "p1", { state: "working", prompt: null, promptDetail: null }),
      }),
    );
    expect(document.title).toBe("(1) PANOPTICON");

    FakeEventSource.latest().emit(
      SESSION_UPDATED,
      JSON.stringify({
        changeId: "web",
        session: heldSession("s2", "p2", { state: "working", prompt: null, promptDetail: null }),
      }),
    );
    expect(document.title).toBe("PANOPTICON");
  });

  it("counts only prompts held for the dashboard that the viewer may answer", async () => {
    const server = signedIn().on("GET", "/api/changes", {
      status: 200,
      body: [
        card({
          changeId: "tags",
          sessions: [
            // In the terminal (not away), and a teammate's (no detail): neither counts.
            heldSession("s1", "p1", {
              promptDetail: { id: "p1", held: false, askedAt: NOW, questions: [] },
            }),
            heldSession("s2", "p2", { memberId: "someone-else", promptDetail: null }),
          ],
        }),
      ],
    });
    const { root } = await startTestShell("/", server);
    await until(() => root.querySelector("article.card") !== null);

    expect(document.title).toBe("PANOPTICON");
  });

  it("leaves the count off when you leave the dashboard", async () => {
    const server = signedIn().on("GET", "/api/changes", {
      status: 200,
      body: [card({ sessions: [heldSession("s1", "p1")] })],
    });
    const { shell } = await startTestShell("/", server);
    await until(() => document.title === "(1) PANOPTICON");

    await shell.router?.navigate("/tokens");

    expect(document.title).toBe("PANOPTICON");
  });

  it("titleWith", () => {
    expect(titleWith(0)).toBe("PANOPTICON");
    expect(titleWith(3)).toBe("(3) PANOPTICON");
  });
});

describe("Browser notifications", () => {
  async function dashboard(notifications: ReturnType<typeof fakeNotifications>, local = {}) {
    const server = signedIn().on("GET", "/api/changes", {
      status: 200,
      body: [card({ changeId: "tags", change: "tags", sessions: [] })],
    });
    const started = await startTestShell("/", server, { notifications: notifications.host, local });
    await until(() => started.root.querySelector("article.card") !== null);
    FakeEventSource.latest().open();
    await until(() => server.to("GET", "/api/changes").length === 2);
    const hold = (promptId: string) =>
      FakeEventSource.latest().emit(
        SESSION_UPDATED,
        JSON.stringify({ changeId: "tags", session: heldSession("s1", promptId) }),
      );
    const toggle = () => q<HTMLButtonElement>(started.root, ".notifications-toggle");
    return { ...started, hold, toggle };
  }

  // Spec live-dashboard: "Needs you", A visit request (the tab title), and Browser notifications.
  function heldVisit(id: string, promptId: string): SessionView {
    return heldSession(id, promptId, {
      state: "waiting-on-visit",
      prompt: { kind: "visit" },
      promptDetail: {
        id: promptId,
        held: true,
        askedAt: NOW,
        visit: { url: "https://github.com/emmz/pan/pull/12", reason: "Review the PR" },
      },
    });
  }

  it("A visit request: counted in the tab title, and its notification says a visit request is waiting", async () => {
    const notifications = fakeNotifications();
    const { toggle } = await dashboard(notifications);
    toggle().click();
    await until(() => toggle().getAttribute("aria-pressed") === "true");

    FakeEventSource.latest().emit(
      SESSION_UPDATED,
      JSON.stringify({ changeId: "tags", session: heldVisit("s1", "v1") }),
    );
    await until(() => document.title === "(1) PANOPTICON");

    expect(notifications.shown).toHaveLength(1);
    expect(notifications.shown[0]?.title).toBe("Needs you: tags");
    expect(notifications.shown[0]?.options.body).toBe("A visit request is waiting for you.");
  });

  it("Never asked: no permission prompt, and no notification, until the member chooses", async () => {
    const notifications = fakeNotifications();
    const { hold, toggle } = await dashboard(notifications);

    hold("p1");

    expect(notifications.asked()).toBe(0);
    expect(notifications.shown).toHaveLength(0);
    expect(toggle().getAttribute("aria-pressed")).toBe("false");
  });

  it("Notification: once on, a newly held prompt raises one notification for its change", async () => {
    const notifications = fakeNotifications();
    const { hold, toggle, local, events } = await dashboard(notifications);
    document.body.append(events);

    toggle().click();
    await until(() => toggle().getAttribute("aria-pressed") === "true");
    expect(notifications.asked()).toBe(1);
    expect(local.getItem(NOTIFICATIONS_KEY)).toBe("on");

    hold("p1");
    hold("p1");

    expect(notifications.shown).toHaveLength(1);
    expect(notifications.shown[0]?.title).toBe("Needs you: tags");
    expect(notifications.shown[0]?.options.tag).toBe("p1");

    // Selecting it brings the dashboard to the card.
    notifications.shown[0]?.click();
    await until(() => document.activeElement?.classList.contains("card-link") === true);
    expect(document.activeElement?.closest("article")?.dataset["changeId"]).toBe("tags");
    events.remove();
  });

  it("stays on in this browser once chosen, without asking again", async () => {
    const notifications = fakeNotifications();
    notifications.grant();
    const { hold, toggle } = await dashboard(notifications, { [NOTIFICATIONS_KEY]: "on" });

    expect(toggle().getAttribute("aria-pressed")).toBe("true");
    hold("p2");

    expect(notifications.asked()).toBe(0);
    expect(notifications.shown).toHaveLength(1);
  });

  it("says so in text when the browser blocks them", async () => {
    const notifications = fakeNotifications("denied");
    const { root, toggle } = await dashboard(notifications);

    toggle().click();
    await until(() => accountError(root) !== "");

    expect(accountError(root)).toBe(
      "This browser blocks notifications from this site. Allow them in its settings, then try again.",
    );
    expect(toggle().getAttribute("aria-pressed")).toBe("false");
  });

  it("turns off again", async () => {
    const notifications = fakeNotifications();
    notifications.grant();
    const { hold, toggle, local } = await dashboard(notifications, { [NOTIFICATIONS_KEY]: "on" });

    toggle().click();
    hold("p3");

    expect(toggle().getAttribute("aria-pressed")).toBe("false");
    expect(local.getItem(NOTIFICATIONS_KEY)).toBeNull();
    expect(notifications.shown).toHaveLength(0);
  });

  it("isn't offered where the browser has no notifications", async () => {
    const { root } = await startTestShell("/", signedIn());

    expect(q<HTMLButtonElement>(root, ".notifications-toggle").hidden).toBe(true);
  });
});

// Spec brand-identity, "Logo beside the wordmark"; logo design D3.
describe("Logo beside the wordmark", () => {
  const homeLink = (root: HTMLElement) =>
    q<HTMLAnchorElement>(root, ".wordmark-heading > a.wordmark");

  function expectLogoThenWordmark(link: HTMLAnchorElement) {
    const [logo, name] = [...link.childNodes];
    expect(logo).toBeInstanceOf(HTMLImageElement);
    const img = logo as HTMLImageElement;
    expect(img.className).toBe("wordmark-logo");
    expect(img.getAttribute("src")).toBe("/logo.png");
    expect(name?.nodeType).toBe(Node.TEXT_NODE);
    expect(name?.textContent).toBe("PANOPTICON");
    expect(link.childNodes).toHaveLength(2);
  }

  it("Signed-in page: the header starts with the logo, then the wordmark, inside the link home", async () => {
    const { root } = await startTestShell("/", signedIn());

    const link = homeLink(root);
    expect(link.getAttribute("href")).toBe("/");
    expectLogoThenWordmark(link);
  });

  it("Sign-in page: the header shows the logo before the wordmark too", async () => {
    const { root, heading } = await startTestShell("/", signedOut());

    expect(heading()).toBe("Sign in");
    expectLogoThenWordmark(homeLink(root));
  });

  it("Screen reader: the link is named PANOPTICON once, and the image adds nothing", async () => {
    const { root } = await startTestShell("/", signedIn());

    const link = homeLink(root);
    const img = q<HTMLImageElement>(link, "img");
    expect(img.getAttribute("alt")).toBe("");
    expect(img.hasAttribute("title")).toBe(false);
    expect(img.hasAttribute("aria-label")).toBe(false);
    expect(link.hasAttribute("aria-label")).toBe(false);
    expect(link.textContent).toBe("PANOPTICON");
  });
});
