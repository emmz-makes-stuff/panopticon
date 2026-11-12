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
});
