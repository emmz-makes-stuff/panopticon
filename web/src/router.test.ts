import { describe, expect, it, vi } from "vitest";
import { createRouter, type View } from "./router";
import { fakeNavigation } from "./testing";

function setup(url: string, guard?: (path: string, search: string) => string | undefined) {
  const nav = fakeNavigation(url);
  const root = document.createElement("main");
  const events = document.createElement("div");
  events.append(root);
  const left = vi.fn();
  const view =
    (name: string): View =>
    ({ root: target, params }) => {
      target.textContent = `${name}${params.toString() ? `?${params.toString()}` : ""}`;
      return () => left(name);
    };
  const router = createRouter({
    root,
    routes: { "/": view("cards"), "/members": view("members"), "/device": view("device") },
    fallback: "/",
    guard,
    history: nav.history,
    location: nav.location,
    events,
  });
  return { nav, root, events, left, router };
}

// Design D18: pushState, a path → view map, and <a data-route> interception.
describe("createRouter", () => {
  it("shows the view for the current path, with its query", async () => {
    const { root, router } = setup("/device?code=BCDF-GHJK");

    await router.start();

    expect(root.textContent).toBe("device?code=BCDF-GHJK");
  });

  it("sends an unknown path to the fallback, replacing the history entry", async () => {
    const { root, nav, router } = setup("/nowhere");

    await router.start();

    expect(root.textContent).toBe("cards");
    expect(nav.history.entries).toEqual(["/"]);
  });

  it("navigates with pushState and tears the old view down", async () => {
    const { root, nav, left, router } = setup("/");
    await router.start();

    await router.navigate("/members");

    expect(root.textContent).toBe("members");
    expect(left).toHaveBeenCalledWith("cards");
    expect(nav.history.entries).toEqual(["/", "/members"]);
  });

  it("handles clicks on data-route links in place, and leaves other links alone", async () => {
    const { root, events, nav, router } = setup("/");
    await router.start();
    const routed = document.createElement("a");
    routed.href = "/members";
    routed.dataset["route"] = "";
    const plain = document.createElement("a");
    plain.href = "/members";
    events.append(routed, plain);

    const plainClick = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0 });
    plain.dispatchEvent(plainClick);
    expect(plainClick.defaultPrevented).toBe(false);

    const click = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0 });
    routed.dispatchEvent(click);
    await Promise.resolve();

    expect(click.defaultPrevented).toBe(true);
    expect(nav.path()).toBe("/members");
    expect(root.textContent).toBe("members");
  });

  it("shows the view again on popstate", async () => {
    const { root, events, nav, router } = setup("/");
    await router.start();
    nav.history.replaceState(null, "", "/members");

    events.dispatchEvent(new Event("popstate"));
    await Promise.resolve();

    expect(root.textContent).toBe("members");
  });

  it("lets a guard redirect before a view shows", async () => {
    const { root, nav, router } = setup("/members", (path) =>
      path === "/members" ? "/device?code=X" : undefined,
    );

    await router.start();

    expect(nav.path()).toBe("/device?code=X");
    expect(root.textContent).toBe("device?code=X");
  });
});
