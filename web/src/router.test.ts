import { describe, expect, it, vi } from "vitest";
import { createRouter, match, type View } from "./router";
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

  it("ignores a popstate that leaves the path and query as shown (an in-page anchor)", async () => {
    const { root, events, left, router } = setup("/members?tab=all");
    await router.start();
    root.textContent = "scrolled to #fn-1";

    events.dispatchEvent(new Event("popstate"));
    await Promise.resolve();

    expect(left).not.toHaveBeenCalled();
    expect(root.textContent).toBe("scrolled to #fn-1");
  });

  it("updates a view in place when only the query changes, if the view can", async () => {
    const nav = fakeNavigation("/changes/c1?doc=a.md");
    const root = document.createElement("main");
    const shows = vi.fn();
    const teardown = vi.fn();
    const update = vi.fn();
    const changeView: View = ({ params }) => {
      shows(params.get("doc"));
      return { teardown, update: (next) => update(next.get("doc")) };
    };
    const router = createRouter({
      root,
      routes: { "/": () => undefined, "/changes/:id": changeView },
      fallback: "/",
      history: nav.history,
      location: nav.location,
      events: document.createElement("div"),
    });
    await router.start();

    await router.navigate("/changes/c1?doc=b.md");
    expect(update).toHaveBeenCalledWith("b.md");
    expect(shows).toHaveBeenCalledTimes(1);
    expect(teardown).not.toHaveBeenCalled();

    await router.navigate("/changes/c2");
    expect(teardown).toHaveBeenCalledTimes(1);
    expect(shows).toHaveBeenLastCalledWith(null);
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

// change-documents-and-history D11: `/changes/<id>` routes by a named segment.
describe("match", () => {
  const view =
    (name: string): View =>
    () => {
      void name;
      return undefined;
    };
  const changes = view("change");
  const history = view("history");
  const routes = { "/": view("cards"), "/history": history, "/changes/:id": changes };

  it("prefers an exact path, then matches named segments, decoded", () => {
    expect(match(routes, "/history")).toEqual({ view: history, route: {} });
    expect(match(routes, "/changes/c1")).toEqual({ view: changes, route: { id: "c1" } });
    expect(match(routes, "/changes/a%20b")).toEqual({ view: changes, route: { id: "a b" } });
  });

  it("doesn't match an empty, extra, undecodable or slash-hiding segment", () => {
    for (const path of [
      "/changes/",
      "/changes",
      "/changes/c1/more",
      "/changes/%E0%A4%A",
      "/changes/a%2Fb",
    ]) {
      expect(match(routes, path), path).toBeUndefined();
    }
  });

  it("gives a matched view its named segments through the router", async () => {
    const nav = fakeNavigation("/changes/c1?doc=design.md");
    const root = document.createElement("main");
    const router = createRouter({
      root,
      routes: {
        "/": () => undefined,
        "/changes/:id": ({ root: target, route, params }) => {
          target.textContent = `${route["id"]} ${params.get("doc")}`;
          return undefined;
        },
      },
      fallback: "/",
      history: nav.history,
      location: nav.location,
      events: document.createElement("div"),
    });

    await router.start();

    expect(root.textContent).toBe("c1 design.md");
  });
});
