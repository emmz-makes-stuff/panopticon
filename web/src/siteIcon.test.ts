import { describe, expect, it } from "vitest";
import indexHtml from "../index.html?raw";

// Spec brand-identity, "Site icon" ("Browser tab"); logo design D2 and D5.

const head = new DOMParser().parseFromString(indexHtml, "text/html").head;
const publicFiles = Object.keys(import.meta.glob("../public/*.png")).map((p) =>
  p.replace("../public", ""),
);

describe("Site icon", () => {
  it("Browser tab: the page declares the 32 px PNG favicon, and the file is in public/", () => {
    const icon = head.querySelector<HTMLLinkElement>('link[rel="icon"]');

    expect(icon?.getAttribute("type")).toBe("image/png");
    expect(icon?.getAttribute("sizes")).toBe("32x32");
    expect(icon?.getAttribute("href")).toBe("/favicon-32.png");
    expect(publicFiles).toContain("/favicon-32.png");
  });

  it("declares the touch icon for home screens, and the file is in public/", () => {
    const touch = head.querySelector<HTMLLinkElement>('link[rel="apple-touch-icon"]');

    expect(touch?.getAttribute("href")).toBe("/apple-touch-icon.png");
    expect(publicFiles).toContain("/apple-touch-icon.png");
  });

  it("ships the header logo the wordmark uses", () => {
    expect(publicFiles).toContain("/logo.png");
  });
});
