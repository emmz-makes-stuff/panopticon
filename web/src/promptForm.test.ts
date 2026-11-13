import { describe, expect, it } from "vitest";
import { createApi, REQUEST_HEADER } from "./api";
import { mountApp } from "./app";
import { renderCard } from "./card";
import { joinAnswer } from "./promptForm";
import { DEFAULT_QUIET } from "./quiet";
import { card, fakeFetch, fakeServer, NOW, q, until } from "./testing";
import type { PromptDetail, PromptQuestion, SessionView } from "./types";

// Spec live-dashboard: "Needs you". Spec remote-answers: "Question forms", "Permission prompts" (the form's side) and
// "Safe rendering". agent-interaction D3, D5.

const EMMZ = "0193f7a0-0000-7000-8000-00000000e001";
const SAM = "0193f7a0-0000-7000-8000-00000000e002";
const PROMPT = "0193f7a0-0000-7000-8000-0000000000a1";
const ANSWER = `/api/prompts/${PROMPT}/answer`;

const AREAS: PromptQuestion = {
  question: "Which areas does this touch?",
  header: "Areas",
  options: [{ label: "Hooks" }, { label: "Web", description: "the dashboard" }, { label: "CLI" }],
  multiSelect: true,
};

const STORE: PromptQuestion = {
  question: "Which store?",
  header: "Store",
  options: [{ label: "Postgres" }, { label: "Redis" }],
  multiSelect: false,
};

const SHIP: PromptQuestion = {
  question: "Ship it?",
  header: "Ship",
  options: [{ label: "Yes" }, { label: "No" }],
  multiSelect: false,
};

function detail(overrides: Partial<PromptDetail> = {}): PromptDetail {
  return { id: PROMPT, held: true, askedAt: NOW.toISOString(), questions: [AREAS], ...overrides };
}

function asking(overrides: Partial<SessionView> = {}): SessionView {
  return {
    id: "s1",
    memberId: EMMZ,
    memberName: "Emmz",
    machine: "mbp",
    state: "waiting-on-question",
    since: NOW.toISOString(),
    lastEventAt: NOW.toISOString(),
    prompt: { kind: "question" },
    promptDetail: detail(),
    ...overrides,
  };
}

function permission(preview: string | null, overrides: Partial<SessionView> = {}): SessionView {
  return asking({
    state: "waiting-on-permission",
    prompt: { kind: "permission", tool: "Bash" },
    promptDetail: detail({ questions: null, preview }),
    ...overrides,
  });
}

function show(session: SessionView, viewerId = EMMZ, status = 200) {
  const server = fakeServer().on("POST", ANSWER, { status, body: { detail: "refused" } });
  const api = createApi({ fetch: server });
  const article = renderCard(card({ sessions: [session] }), NOW, DEFAULT_QUIET, {
    id: viewerId,
    api,
  });
  document.body.append(article);
  const posted = () => server.to("POST", ANSWER);
  const marker = () => article.querySelector(".card-session-marker")?.textContent ?? null;
  const form = () => article.querySelector<HTMLFormElement>(".prompt-form");
  const held = () => q<HTMLFormElement>(article, ".prompt-form");
  const error = () =>
    article.querySelector<HTMLElement>(".prompt-error:not([hidden])")?.textContent;
  return { article, server, posted, marker, form, held, error };
}

function check(root: ParentNode, label: string): void {
  const option = [...root.querySelectorAll<HTMLInputElement>(".prompt-option input")].find(
    (input) => input.value === label,
  );
  if (!option) {
    throw new Error(`no option "${label}"`);
  }

  option.click();
}

function type(input: HTMLInputElement | HTMLTextAreaElement, text: string): void {
  input.value = text;
  input.dispatchEvent(new Event("input", { bubbles: true }));
}

function button(root: ParentNode, selector: string): HTMLButtonElement {
  return q<HTMLButtonElement>(root, selector);
}

describe("Needs you", () => {
  it("Owner's view: a held question shows Needs you with its form", () => {
    const { article, marker, form } = show(asking());

    expect(marker()).toBe("Needs you");
    expect(form()).not.toBeNull();
    expect(article.classList.contains("card--needs-you")).toBe(true);
  });

  it("Not away: the question and 'answer in the terminal', with no form", () => {
    const { article, marker, form } = show(asking({ promptDetail: detail({ held: false }) }));

    expect(marker()).toBe("Needs you");
    expect(form()).toBeNull();
    expect(q(article, ".card-session-question").textContent).toBe("Which areas does this touch?");
    expect(q(article, ".card-session-terminal").textContent).toBe("answer in the terminal");
  });

  it("A teammate's view: 'Needs Emmz: question', with no form and no question text", () => {
    const { article, marker, form } = show(asking({ promptDetail: null }), SAM);

    expect(marker()).toBe("Needs Emmz: question");
    expect(form()).toBeNull();
    expect(article.textContent).not.toContain("Which areas");
    expect(article.classList.contains("card--needs-you")).toBe(false);
  });

  it("names a permission prompt's tool to a teammate", () => {
    const { marker } = show(permission(null, { promptDetail: null }), SAM);

    expect(marker()).toBe("Needs Emmz: permission for Bash");
  });

  it("a prompt over the limits still needs someone: you for its member, the member's name for anyone else", () => {
    expect(show(asking({ prompt: null, promptDetail: null })).marker()).toBe("Needs you");
    expect(show(asking({ prompt: null, promptDetail: null }), SAM).marker()).toBe("Needs Emmz");
  });

  it("a session waiting after its turn ended needs nobody", () => {
    const { article, marker } = show(
      asking({ state: "waiting-on-user", prompt: null, promptDetail: null }),
    );

    expect(marker()).toBeNull();
    expect(article.classList.contains("card--needs-you")).toBe(false);
  });

  it("an answerable Project owner sees Needs you on someone else's session", () => {
    expect(show(asking(), SAM).marker()).toBe("Needs you");
  });
});

describe("Question forms", () => {
  it("Multiple choice: picking Hooks and Web answers 'Hooks, Web'", async () => {
    const { held, posted } = show(asking());
    const shown = held();

    check(shown, "Hooks");
    check(shown, "Web");
    button(shown, ".prompt-send").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({
      answers: { "Which areas does this touch?": "Hooks, Web" },
    });
    expect(posted()[0]?.headers[REQUEST_HEADER]).toBe("1");
    await until(() => shown.dataset["answered"] === "true");
    expect(q(shown, ".prompt-status").textContent).toBe("Answered. The session carries on.");
  });

  it("Other: typed text is the answer, in place of a single choice", async () => {
    const { held, posted } = show(asking({ promptDetail: detail({ questions: [STORE] }) }));
    const shown = held();

    check(shown, "Postgres");
    type(q<HTMLInputElement>(shown, ".prompt-other"), "Neither, use SQLite");
    button(shown, ".prompt-send").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ answers: { "Which store?": "Neither, use SQLite" } });
  });

  it("Other with multiple choice goes in as one more item", () => {
    expect(joinAnswer(["Hooks"], " Docs ", true)).toBe("Hooks, Docs");
    expect(joinAnswer([], "", true)).toBe("");
    expect(joinAnswer(["Postgres"], "", false)).toBe("Postgres");
  });

  it("Yes or no: two buttons and an Other answer", async () => {
    const { held, posted } = show(asking({ promptDetail: detail({ questions: [SHIP] }) }));
    const shown = held();

    const toggles = [...shown.querySelectorAll<HTMLButtonElement>(".prompt-toggle")];
    expect(toggles.map((t) => [t.textContent, t.getAttribute("aria-pressed")])).toEqual([
      ["Yes", "false"],
      ["No", "false"],
    ]);
    expect(shown.querySelector(".prompt-option")).toBeNull();
    expect(shown.querySelector(".prompt-other")).not.toBeNull();

    button(shown, ".prompt-toggle").click();
    expect(toggles[0]?.getAttribute("aria-pressed")).toBe("true");
    button(shown, ".prompt-send").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ answers: { "Ship it?": "Yes" } });
  });

  it("shows every question's header, options and descriptions, as text", () => {
    const { held } = show(asking({ promptDetail: detail({ questions: [AREAS, STORE] }) }));
    const shown = held();

    const legends = [...shown.querySelectorAll(".prompt-legend")].map((l) => l.textContent);
    expect(legends).toEqual(["Areas Which areas does this touch?", "Store Which store?"]);
    expect(shown.querySelectorAll('input[type="checkbox"]')).toHaveLength(3);
    expect(shown.querySelectorAll('input[type="radio"]')).toHaveLength(2);
    expect(q(shown, ".prompt-option-description").textContent).toBe("the dashboard");
  });

  it("every question must be answered: an inline error, and nothing posted", async () => {
    const { held, posted, error } = show(
      asking({ promptDetail: detail({ questions: [AREAS, STORE] }) }),
    );
    const shown = held();

    check(shown, "Hooks");
    button(shown, ".prompt-send").click();
    await until(() => error() !== undefined);

    expect(error()).toBe('Answer "Store" first.');
    expect(posted()).toHaveLength(0);
  });
});

describe("Safe rendering", () => {
  it("Script in a preview: the server's safe HTML goes in as is, so the script shows as text and runs nothing", () => {
    // What DocumentRenderer makes of "<script>alert(1)</script>" (PromptQuestionViewTests).
    const question: PromptQuestion = {
      ...STORE,
      options: [
        { label: "Postgres", previewHtml: "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n" },
      ],
    };
    const { held } = show(asking({ promptDetail: detail({ questions: [question] }) }));

    const preview = q(held(), ".prompt-preview");
    expect(preview.querySelector("script")).toBeNull();
    expect(preview.textContent).toContain("<script>alert(1)</script>");
  });

  it("a permission prompt's input is text in a <pre>, never markup", () => {
    const { held } = show(permission('<script>alert(1)</script> && echo "<b>hi</b>"'));

    const input = q(held(), "pre.prompt-input");
    expect(input.textContent).toBe('<script>alert(1)</script> && echo "<b>hi</b>"');
    expect(input.children).toHaveLength(0);
  });

  it("names and question text are text too", () => {
    const { article } = show(
      asking({
        memberName: "<i>Emmz</i>",
        promptDetail: detail({ held: false, questions: [{ ...STORE, question: "<b>Which?</b>" }] }),
      }),
    );

    expect(article.querySelector("i, b")).toBeNull();
    expect(q(article, ".card-session-question").textContent).toBe("<b>Which?</b>");
  });
});

describe("Permission prompts: the form", () => {
  it("shows the tool and its input, and Allow posts allow once", async () => {
    const { held, posted } = show(permission("make e2e"));
    const shown = held();

    expect(q(shown, ".prompt-text").textContent).toBe("Permission for Bash");
    button(shown, ".prompt-allow").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ allow: true });
  });

  it("offers Allow only once the input is there", () => {
    const { held } = show(permission(null));
    const shown = held();

    expect(button(shown, ".prompt-allow").disabled).toBe(true);
    expect(button(shown, ".prompt-deny").disabled).toBe(false);
    expect(q(shown, ".prompt-pending").textContent).toBe("Waiting for the tool's input.");
  });

  it("Deny with a reason sends the message", async () => {
    const { held, posted } = show(permission("make e2e"));
    const shown = held();

    const message = q<HTMLTextAreaElement>(shown, ".prompt-message");
    expect(message.maxLength).toBe(500);
    type(message, "  not now, the image is stale ");
    button(shown, ".prompt-deny").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ allow: false, message: "not now, the image is stale" });
  });

  it("Deny without a message sends none", async () => {
    const { held, posted } = show(permission("make e2e"));

    button(held(), ".prompt-deny").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ allow: false });
  });
});

// Spec remote-answers: "Visit request form"; visit-requests D7.
const PR = "https://github.com/emmz/pan/pull/12";

function visiting(url = PR, overrides: Partial<SessionView> = {}): SessionView {
  return asking({
    state: "waiting-on-visit",
    prompt: { kind: "visit" },
    promptDetail: detail({
      questions: null,
      visit: { url, reason: "Review the PR", action: "Approve it if you're happy" },
    }),
    ...overrides,
  });
}

describe("Visit request form", () => {
  it("shows the reason, the action and the URL in full as a link to a new tab with no opener or referrer", () => {
    const { held, marker } = show(visiting());
    const shown = held();

    expect(marker()).toBe("Needs you");
    expect(shown.classList.contains("prompt-form--visit")).toBe(true);
    expect(q(shown, ".prompt-text").textContent).toBe("Review the PR");
    expect(q(shown, ".prompt-visit-action").textContent).toBe("Approve it if you're happy");
    const link = q<HTMLAnchorElement>(shown, "a.prompt-visit-link");
    expect(link.textContent).toBe(PR);
    expect(link.href).toBe(PR);
    expect(link.target).toBe("_blank");
    expect(link.rel).toBe("noopener noreferrer");
    expect(q(shown, ".prompt-visit-hint").textContent).toBe(" (opens in a new tab)");
  });

  it.each(["javascript:alert(1)", "file:///Users/emmz/report.html", "not a url"])(
    "shows %s as text, never as a link",
    (url) => {
      const { held } = show(visiting(url));
      const shown = held();

      expect(shown.querySelector("a")).toBeNull();
      expect(q(shown, ".prompt-visit-link").textContent).toBe(url);
    },
  );

  it("Done with a note sends visited true and the trimmed note", async () => {
    const { held, posted } = show(visiting());
    const shown = held();

    const note = q<HTMLTextAreaElement>(shown, ".prompt-message");
    expect(note.maxLength).toBe(1000);
    type(note, "  approved, merge when green ");
    button(shown, ".prompt-done").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ visited: true, message: "approved, merge when green" });
  });

  it("Can't with no note sends visited false and no message", async () => {
    const { held, posted } = show(visiting());

    button(held(), ".prompt-cant").click();
    await until(() => posted().length === 1);

    expect(posted()[0]?.body).toEqual({ visited: false });
  });

  it("the reason, action and URL are text, never markup", () => {
    const { held } = show(
      asking({
        state: "waiting-on-visit",
        prompt: { kind: "visit" },
        promptDetail: detail({
          questions: null,
          visit: { url: PR, reason: "<img src=x onerror=alert(1)>", action: "<b>bold</b>" },
        }),
      }),
    );
    const shown = held();

    expect(shown.querySelector("img, b")).toBeNull();
    expect(q(shown, ".prompt-text").textContent).toBe("<img src=x onerror=alert(1)>");
  });
});

describe("answer errors, inline", () => {
  it.each([
    [403, "Only the session's member and its Project's owners can answer this."],
    [404, "This prompt has gone."],
    [409, "This prompt has already been answered, or has gone back to the terminal."],
  ])("%i says why in text, and the form stops taking answers", async (status, text) => {
    const { held, error } = show(permission("make e2e"), EMMZ, status);
    const shown = held();

    button(shown, ".prompt-allow").click();
    await until(() => error() !== undefined);

    expect(error()).toBe(text);
    expect(button(shown, ".prompt-allow").disabled).toBe(true);
    expect(button(shown, ".prompt-deny").disabled).toBe(true);
  });

  it("any other failure gives the server's detail and lets you try again", async () => {
    const { held, error } = show(permission("make e2e"), EMMZ, 400);
    const shown = held();

    button(shown, ".prompt-deny").click();
    await until(() => error() !== undefined);

    expect(error()).toBe("refused");
    expect(button(shown, ".prompt-deny").disabled).toBe(false);
    expect(button(shown, ".prompt-allow").disabled).toBe(false);
  });
});

describe("a half-typed answer", () => {
  it("survives a new report rebuilding the card", async () => {
    const root = document.createElement("main");
    const initial = card({ changeId: "tags", sessions: [asking()] });
    const app = await mountApp(root, {
      fetch: fakeFetch([initial]),
      now: () => NOW,
      viewerId: EMMZ,
    });
    const before = q<HTMLFormElement>(root, ".prompt-form");
    type(q<HTMLInputElement>(before, ".prompt-other"), "Docs");

    app.upsert({ ...initial, lastReportedAt: NOW.toISOString(), overall: { done: 8, total: 20 } });

    const after = q<HTMLFormElement>(root, ".prompt-form");
    expect(after).toBe(before);
    expect(q<HTMLInputElement>(after, ".prompt-other").value).toBe("Docs");
  });
});
