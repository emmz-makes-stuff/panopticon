import { describe, expect, it } from "vitest";
import { mountApp } from "./app";

describe("mountApp", () => {
  it("renders the product name", () => {
    const root = document.createElement("main");

    mountApp(root);

    expect(root.querySelector("h1")?.textContent).toBe("Panopticon");
  });
});
