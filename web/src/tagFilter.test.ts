import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { card, FakeEventSource, q, settle } from "./testing";
import { signedIn, startTestShell } from "./testShell";
import type { ChangeCard } from "./types";

const PAN = { id: "0190f5a2-0000-7000-8000-0000000000a1", code: "PAN", name: "Panopticon" };
const BUG = { id: "0190f5a2-0000-7000-8000-0000000000b1", key: "bug", value: null };
const RUST = { id: "0190f5a2-0000-7000-8000-0000000000b2", key: "language", value: "rust" };
const enc = encodeURIComponent;

const CARDS: ChangeCard[] = [
  card({
    changeId: "a",
    change: "team-auth",
    project: PAN,
    projectTags: [BUG],
    personalTags: [
      { key: "dev", value: null },
      { key: "language", value: "csharp" },
    ],
  }),
  card({
    changeId: "b",
    change: "site",
    project: PAN,
    projectTags: [BUG, RUST],
    lastReportedAt: "2026-11-08T11:50:00Z",
  }),
  card({ changeId: "c", change: "plain", lastReportedAt: "2026-11-08T11:40:00Z" }),
];

const serverWith = (cards: ChangeCard[] = CARDS) =>
  signedIn().on("GET", "/api/changes", { status: 200, body: cards });

// In the document, so focus moves and checkboxes toggle as in a browser.
const attached: HTMLElement[] = [];
beforeEach(() => {
  FakeEventSource.instances.length = 0;
});
afterEach(() => {
  for (const node of attached.splice(0)) {
    node.remove();
  }
});

async function start(url = "/", cards?: ChangeCard[]) {
  const shell = await startTestShell(url, serverWith(cards));
  document.body.append(shell.events);
  attached.push(shell.events);
  return shell;
}

const names = (root: HTMLElement) =>
  [...root.querySelectorAll("article.card .card-change")].map((c) => c.textContent);
const tagButton = (root: HTMLElement) => q<HTMLButtonElement>(root, ".filter-tags-button");
const popover = (root: HTMLElement) => q(root, ".tag-filter-popover");
const groups = (root: HTMLElement) =>
  [...root.querySelectorAll<HTMLElement>(".tag-filter-group-box")]
    .filter((g) => !g.hidden)
    .map((g) => [
      g.querySelector(".tag-filter-group")?.textContent,
      ...[...g.querySelectorAll<HTMLElement>(".tag-filter-option")]
        .filter((o) => !o.hidden)
        .map(
          (o) =>
            `${o.classList.contains("tag-filter-option--value") ? "  " : ""}${o.querySelector("input")?.checked ? "☑" : "☐"} ${o.textContent}`,
        ),
    ]);
const chosenChips = (root: HTMLElement) =>
  [...root.querySelectorAll(".filter-tags-chosen-tag")].map(
    (li) => li.querySelector(".tag-chip")?.textContent,
  );

function toggleTag(root: HTMLElement, text: string) {
  const option = [...root.querySelectorAll(".tag-filter-option")].find(
    (o) => o.querySelector(".tag-filter-text")?.textContent === text,
  );
  const box = option?.querySelector("input");
  if (!box) {
    throw new Error(`No option ${text}`);
  }

  box.click();
}

// Spec change-filters, "Tag filter choices" and "Filter by tags"; web/DESIGN.md "The Tags filter".
describe("the dashboard's Tags filter", () => {
  it("reads Any tags, and opens to the grouped choices, with the search focused", async () => {
    const { root } = await start();
    expect(tagButton(root).textContent).toBe("Any tags");
    expect(tagButton(root).classList).not.toContain("filter-select--set");
    expect(popover(root).hidden).toBe(true);
    expect(q(root, ".filter-tags-chosen").hidden).toBe(true);

    tagButton(root).click();

    expect(popover(root).hidden).toBe(false);
    expect(tagButton(root).getAttribute("aria-expanded")).toBe("true");
    expect(document.activeElement).toBe(q(root, ".tag-filter-popover input[type=search]"));
    expect(groups(root)).toEqual([
      ["Yours", "☐ dev", "☐ languageany value", "  ☐ language: csharp"],
      ["PAN", "☐ bug", "☐ languageany value", "  ☐ language: rust"],
    ]);
    expect(q(root, ".tag-filter-group-box").getAttribute("role")).toBe("group");
  });

  it("finds a tag by its text, hiding groups with nothing to show", async () => {
    const { root } = await start();
    tagButton(root).click();
    const search = q<HTMLInputElement>(root, ".tag-filter-popover input[type=search]");

    search.value = "RUST";
    search.dispatchEvent(new Event("input"));

    expect(groups(root)).toEqual([["PAN", "  ☐ language: rust"]]);
  });

  it("Escape closes it and returns focus to the button; a click outside closes it too", async () => {
    const { root } = await start();
    tagButton(root).click();
    popover(root).dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

    expect(popover(root).hidden).toBe(true);
    expect(document.activeElement).toBe(tagButton(root));

    tagButton(root).click();
    q(root, ".grid").click();
    expect(popover(root).hidden).toBe(true);
    expect(tagButton(root).getAttribute("aria-expanded")).toBe("false");
  });

  it("Two tags: each choice applies at once, writing the address, and only changes with both show", async () => {
    const { root, nav } = await start();
    tagButton(root).click();

    toggleTag(root, "bug");
    await settle();
    expect(nav.path()).toBe(`/?tag=${enc(`t:${BUG.id}`)}`);
    expect(names(root)).toEqual(["team-auth", "site"]);
    expect(tagButton(root).textContent).toBe("1 tag");
    expect(tagButton(root).classList).toContain("filter-select--set");

    toggleTag(root, "dev");
    await settle();
    expect(nav.path()).toBe(`/?tag=p%3Adev&tag=${enc(`t:${BUG.id}`)}`);
    expect(names(root)).toEqual(["team-auth"]);
    expect(tagButton(root).textContent).toBe("2 tags");
    expect(popover(root).hidden).toBe(false);
    expect(q(root, ".filter-tags-lead").textContent).toBe("Showing changes with all of:");
    expect(chosenChips(root)).toEqual(["dev (only you see this)", "PAN/bug"]);
  });

  it("Key alone: PAN/language any value matches every value, and its chip says any", async () => {
    const { root } = await start(`/?tag=${enc(`k:${PAN.id}:language`)}`);

    expect(names(root)).toEqual(["site"]);
    expect(chosenChips(root)).toEqual(["PAN/language: any"]);
  });

  it("Removing one tag: its × keeps the other", async () => {
    const { root, nav } = await start(`/?tag=p%3Adev&tag=${enc(`t:${BUG.id}`)}`);
    const remove = q(root, ".filter-tags-remove");
    expect(remove.getAttribute("aria-label")).toBe("Remove dev from the filter");

    remove.click();
    await settle();

    expect(nav.path()).toBe(`/?tag=${enc(`t:${BUG.id}`)}`);
    expect(names(root)).toEqual(["team-auth", "site"]);
  });

  it("Back and Forward move between tag choices, with no reload", async () => {
    const { root, nav, events } = await start();
    tagButton(root).click();
    toggleTag(root, "bug");
    await settle();
    toggleTag(root, "dev");
    await settle();

    nav.history.replaceState(null, "", `/?tag=${enc(`t:${BUG.id}`)}`);
    events.dispatchEvent(new Event("popstate"));
    await settle();
    expect(names(root)).toEqual(["team-auth", "site"]);
    expect(groups(root)[0]?.[1]).toBe("☐ dev");
    expect(tagButton(root).textContent).toBe("1 tag");

    nav.history.replaceState(null, "", "/");
    events.dispatchEvent(new Event("popstate"));
    await settle();
    expect(names(root)).toEqual(["team-auth", "site", "plain"]);
    expect(tagButton(root).textContent).toBe("Any tags");
    expect(q(root, ".filter-tags-chosen").hidden).toBe(true);
  });

  it("A deleted tag in the address: an Unknown tag chip, nothing matches, and Clear filters clears it", async () => {
    const { root, nav } = await start("/?tag=t%3A0190f5a2-0000-7000-8000-0000000000ff");

    expect(chosenChips(root)).toEqual(["Unknown tag"]);
    expect(q(root, ".filter-tags-remove").getAttribute("aria-label")).toBe(
      "Remove Unknown tag from the filter",
    );
    expect(root.querySelector(".no-match")).not.toBeNull();

    q(root, ".filter-clear").click();
    await settle();
    expect(nav.path()).toBe("/");
    expect(names(root)).toHaveLength(3);
  });

  it("Tags with another filter: a select keeps the chosen tags in the address", async () => {
    const { root, nav } = await start(`/?tag=${enc(`t:${BUG.id}`)}`);
    const select = q<HTMLSelectElement>(root, "select[name=client]");
    select.value = "claude-code";
    select.dispatchEvent(new Event("change"));
    await settle();

    expect(nav.path()).toBe(`/?client=claude-code&tag=${enc(`t:${BUG.id}`)}`);
  });

  it("says when the address had a malformed tag, or more than 20", async () => {
    const malformed = await start("/?tag=nonsense&tag=p%3Adev");
    expect(q(malformed.root, ".filter-note").textContent).toBe(
      "Dropped a tag filter that wasn't valid.",
    );
    expect(chosenChips(malformed.root)).toEqual(["dev (only you see this)"]);

    const many = Array.from({ length: 21 }, (_, n) => `tag=p%3Ak${n}`).join("&");
    const tooMany = await start(`/?${many}`);
    expect(q(tooMany.root, ".filter-note").textContent).toBe("Kept the first 20 tags.");
    expect(chosenChips(tooMany.root)).toHaveLength(20);
  });

  it("No tags anywhere: the control is there, and opens to say so", async () => {
    const { root } = await start("/", [card({ changeId: "c", change: "plain" })]);
    tagButton(root).click();

    expect(q(root, ".tag-filter-empty").textContent).toBe("None of these changes have tags.");
  });
});
