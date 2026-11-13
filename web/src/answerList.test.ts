import { describe, expect, it } from "vitest";
import { answerSummary } from "./answerList";
import type { AnswerEntry } from "./types";

// Spec remote-answers: "Answers are attributed". The list reads as the spec words it.

function entry(overrides: Partial<AnswerEntry>): AnswerEntry {
  return {
    id: "a1",
    changeId: "c1",
    sessionMember: "Emmz",
    answeredBy: "Bob",
    answeredAt: "2026-11-11T10:00:00Z",
    kind: "permission",
    ...overrides,
  };
}

describe("answerSummary", () => {
  it("names who allowed which tool for whom", () => {
    expect(answerSummary(entry({ tool: "Bash", outcome: "allow" }))).toBe(
      "Bob allowed Bash for Emmz",
    );
  });

  it("names who denied which tool for whom", () => {
    expect(answerSummary(entry({ tool: "Bash", outcome: "deny" }))).toBe(
      "Bob denied Bash for Emmz",
    );
  });

  it("says a tool when the permission prompt didn't name one", () => {
    expect(answerSummary(entry({ tool: null, outcome: "allow" }))).toBe(
      "Bob allowed a tool for Emmz",
    );
  });

  it("names who answered a question for whom", () => {
    expect(answerSummary(entry({ kind: "question" }))).toBe("Bob answered a question for Emmz");
  });

  // Spec remote-answers: "Visit answers are attributed".
  it("Recorded: a visit's URL and Done, for a viewer who could have answered", () => {
    const pr = "https://github.com/emmz/pan/pull/12";
    expect(
      answerSummary(entry({ kind: "visit", answeredBy: "Emmz", url: pr, outcome: "done" })),
    ).toBe(`Emmz visited ${pr} for Emmz: done`);
    expect(answerSummary(entry({ kind: "visit", url: pr, outcome: "cant" }))).toBe(
      `Bob visited ${pr} for Emmz: can't`,
    );
  });

  it("Recorded: only that a visit request was answered, for anyone else", () => {
    expect(
      answerSummary(entry({ kind: "visit", answeredBy: "Emmz", url: null, outcome: "done" })),
    ).toBe("Emmz answered a visit request for Emmz");
  });
});
