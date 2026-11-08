import { describe, expect, it } from "vitest";
import { savedReturn, signedOut, startTestShell } from "../testShell";
import { RETURN_TO_KEY, RETURN_TO_LIFETIME_MS } from "../returnPath";
import { card, MEMBER, until, q } from "../testing";

/** A saved return path, as the shell stores it, `ageMs` before the test shell's clock (2026-11-08T12:00:00Z). */
const saved = (path: string, ageMs = 0) =>
  JSON.stringify({ path, savedAt: new Date("2026-11-08T12:00:00Z").getTime() - ageMs });
import { SIGN_IN_EMAIL_KEY } from "./signIn";

async function submit(root: HTMLElement, email: string) {
  q<HTMLInputElement>(root, "input[name=email]").value = email;
  q(root, "form").dispatchEvent(new Event("submit", { cancelable: true }));
}

// Spec dashboard-sign-in: "Requesting a sign-in link", "Confirming before signing in", "Sign-in links".
describe("/sign-in", () => {
  it("Member asks for a link: posts the address and says to check their email", async () => {
    const server = signedOut().on("POST", "/api/auth/sign-in", {
      status: 202,
      body: { message: "Check your email." },
    });
    const { root, heading, local } = await startTestShell("/sign-in", server);

    await submit(root, "  emmz@example.com ");
    await until(() => heading() === "Check your email");

    expect(server.to("POST", "/api/auth/sign-in")[0]?.body).toEqual({ email: "emmz@example.com" });
    expect(root.querySelector(".page p")?.textContent).toContain("emmz@example.com");
    expect(local.getItem(SIGN_IN_EMAIL_KEY)).toBe("emmz@example.com");
  });

  it("Stranger asks for a link: the page reads exactly as it does for a member", async () => {
    const pageFor = async (email: string) => {
      const server = signedOut().on("POST", "/api/auth/sign-in", { status: 202, body: {} });
      const { root, heading } = await startTestShell("/sign-in", server);
      await submit(root, email);
      await until(() => heading() === "Check your email");
      return q(root, ".view").innerHTML.replace(email, "ADDRESS");
    };

    expect(await pageFor("stranger@example.com")).toBe(await pageFor("emmz@example.com"));
  });

  it("shows the server's message for an invalid address or too many requests", async () => {
    const server = signedOut().on("POST", "/api/auth/sign-in", {
      status: 429,
      body: { detail: "Too many sign-in requests. Try again in a few minutes." },
    });
    const { root, heading } = await startTestShell("/sign-in", server);

    await submit(root, "emmz@example.com");
    await until(() => !!root.querySelector(".form-error")?.textContent);

    expect(heading()).toBe("Sign in");
    expect(root.querySelector(".form-error")?.textContent).toBe(
      "Too many sign-in requests. Try again in a few minutes.",
    );
  });

  it("asks for an address before posting nothing", async () => {
    const server = signedOut();
    const { root } = await startTestShell("/sign-in", server);

    await submit(root, "   ");

    expect(server.to("POST", "/api/auth/sign-in")).toHaveLength(0);
    expect(root.querySelector(".form-error")?.textContent).toBe("Enter your email address.");
  });
});

describe("/sign-in/confirm", () => {
  it("Scanner opens the link: opening it posts nothing, and takes the secret off the address bar", async () => {
    const server = signedOut();
    const { heading, nav } = await startTestShell("/sign-in/confirm#the-secret", server);

    expect(heading()).toBe("Sign in to a PANOPTICON?");
    expect(server.to("POST", "/api/auth/confirm")).toHaveLength(0);
    expect(nav.location.hash).toBe("");
  });

  it("names the address when this browser asked for the link", async () => {
    const { heading } = await startTestShell("/sign-in/confirm#s", signedOut(), {
      local: { [SIGN_IN_EMAIL_KEY]: "emmz@example.com" },
    });

    expect(heading()).toBe("Sign in as emmz@example.com?");
  });

  it("confirming spends the link, signs in and goes where you were going", async () => {
    const server = signedOut()
      .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
      .on("GET", "/api/members", { status: 200, body: [] });
    const { root, nav, local } = await startTestShell("/sign-in/confirm#the-secret", server, {
      local: { [RETURN_TO_KEY]: saved("/members") },
    });

    q<HTMLButtonElement>(root, ".page button").click();
    await until(() => nav.path() === "/members");

    expect(server.to("POST", "/api/auth/confirm")[0]?.body).toEqual({ secret: "the-secret" });
    expect(nav.path()).toBe("/members");
    expect(root.querySelector(".account-name")?.textContent).toBe("Emmz");
    expect(savedReturn(local)).toBeNull();
  });

  it("ignores a return path older than its lifetime, and goes to the cards", async () => {
    const server = signedOut()
      .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
      .on("GET", "/api/changes", { status: 200, body: [card()] });
    const { root, nav, local } = await startTestShell("/sign-in/confirm#the-secret", server, {
      local: { [RETURN_TO_KEY]: saved("/members", RETURN_TO_LIFETIME_MS + 1) },
    });

    q<HTMLButtonElement>(root, ".page button").click();
    await until(() => nav.path() === "/");

    expect(savedReturn(local)).toBeNull();
  });

  it.each(["//evil.example/", "/\\evil.example/", "https://evil.example/"])(
    "ignores a return path that isn't on this site (%s)",
    async (path) => {
      const server = signedOut()
        .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
        .on("GET", "/api/changes", { status: 200, body: [card()] });
      const { root, nav } = await startTestShell("/sign-in/confirm#the-secret", server, {
        local: { [RETURN_TO_KEY]: saved(path) },
      });

      q<HTMLButtonElement>(root, ".page button").click();
      await until(() => nav.path() === "/");

      expect([nav.location.origin, nav.path()]).toEqual(["https://panopticon.test", "/"]);
    },
  );

  it("keeps a same-site return path's query", async () => {
    const server = signedOut()
      .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
      .on("GET", "/api/device/BCDF-GHJK", {
        status: 200,
        body: { userCode: "BCDF-GHJK", machineName: "vm-1", state: "pending" },
      });
    const { root, nav } = await startTestShell("/sign-in/confirm#the-secret", server, {
      local: { [RETURN_TO_KEY]: saved("/device?code=BCDF-GHJK") },
    });

    q<HTMLButtonElement>(root, ".page button").click();
    await until(() => nav.path() === "/device?code=BCDF-GHJK");

    expect(nav.path()).toBe("/device?code=BCDF-GHJK");
  });

  it("says what the server said when confirming fails with something other than a refused link", async () => {
    const server = signedOut().on("POST", "/api/auth/confirm", {
      status: 429,
      body: { detail: "Too many requests. Try again in a few minutes." },
    });
    const { root } = await startTestShell("/sign-in/confirm#s", server);

    q<HTMLButtonElement>(root, ".page button").click();
    await until(() => !!root.querySelector(".page .form-error")?.textContent);

    expect(root.querySelector(".page .form-error")?.textContent).toBe(
      "Too many requests. Try again in a few minutes.",
    );
    expect(q<HTMLButtonElement>(root, ".page button").disabled).toBe(false);
  });

  it("goes to the cards when there's nowhere to return to", async () => {
    const server = signedOut()
      .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
      .on("GET", "/api/changes", { status: 200, body: [card()] });
    const { root, nav } = await startTestShell("/sign-in/confirm#s", server);

    q<HTMLButtonElement>(root, ".page button").click();
    await until(() => root.querySelectorAll("article.card").length === 1);

    expect(nav.path()).toBe("/");
  });

  it.each([
    ["used", "This sign-in link has already been used. Ask for a new one."],
    ["expired", "This sign-in link has expired. Ask for a new one."],
  ])("Link %s: says so and offers to send a new one", async (reason, detail) => {
    const server = signedOut().on("POST", "/api/auth/confirm", {
      status: 400,
      body: { detail, reason },
    });
    const { root, heading } = await startTestShell("/sign-in/confirm#s", server);

    q<HTMLButtonElement>(root, ".page button").click();
    await until(() => heading() === "Can't sign in");

    expect(root.querySelector(".page p")?.textContent).toBe(detail);
    const resend = root.querySelector<HTMLAnchorElement>(".page a");
    expect([resend?.textContent, resend?.getAttribute("href")]).toEqual([
      "Send me a new link",
      "/sign-in",
    ]);
  });

  it("blocked storage still lets you ask for a link and sign in", async () => {
    const blocked = {
      getItem: (): string | null => {
        throw new DOMException("The operation is insecure.", "SecurityError");
      },
      setItem: () => {
        throw new DOMException("The operation is insecure.", "SecurityError");
      },
      removeItem: () => {
        throw new DOMException("The operation is insecure.", "SecurityError");
      },
    };
    const server = signedOut()
      .on("POST", "/api/auth/sign-in", { status: 202, body: {} })
      .on("POST", "/api/auth/confirm", { status: 200, body: MEMBER })
      .on("GET", "/api/changes", { status: 200, body: [card()] });

    const asking = await startTestShell("/members", server, { storage: blocked });
    expect(asking.heading()).toBe("Sign in");
    await submit(asking.root, "emmz@example.com");
    await until(() => asking.heading() === "Check your email");

    const confirming = await startTestShell("/sign-in/confirm#s", server, { storage: blocked });
    expect(confirming.heading()).toBe("Sign in to a PANOPTICON?");
    q<HTMLButtonElement>(confirming.root, ".page button").click();
    await until(() => confirming.nav.path() === "/");
    expect(confirming.root.querySelector(".account-name")?.textContent).toBe("Emmz");
  });

  it("a link with no secret says so and offers a new one", async () => {
    const { heading, root } = await startTestShell("/sign-in/confirm", signedOut());

    expect(heading()).toBe("Can't sign in");
    expect(root.querySelector(".page a")?.getAttribute("href")).toBe("/sign-in");
  });
});
