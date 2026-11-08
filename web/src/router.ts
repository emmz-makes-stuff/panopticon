/**
 * A tiny client-side router (design D18): `history.pushState`, a path → view map, and interception of clicks on
 * `<a data-route>`. There's no framework (ADR-0002).
 */

/** What a view gets: where to render, the query string, and a way to move on. */
export interface ViewContext {
  root: HTMLElement;
  params: URLSearchParams;
  navigate(path: string, options?: { replace?: boolean }): void;
}

/** What a view may return: a teardown, run when the router leaves it. */
export type Teardown = () => void;

/** A view renders into `ctx.root`, and may return a teardown. */
export type View = (ctx: ViewContext) => Teardown | undefined | Promise<Teardown | undefined>;

/** The parts of `window.history` and `window.location` the router uses, so tests can supply fakes. */
export interface HistoryLike {
  pushState(data: unknown, unused: string, url: string): void;
  replaceState(data: unknown, unused: string, url: string): void;
}

export interface LocationLike {
  readonly pathname: string;
  readonly search: string;
}

export interface RouterOptions {
  root: HTMLElement;
  routes: Readonly<Record<string, View>>;
  /** The path an unknown path shows instead. */
  fallback: string;
  /**
   * Runs before every view, with the path about to show. It can answer another path to show instead (a redirect,
   * e.g. to sign in), or `undefined` to carry on.
   */
  guard?: ((path: string, search: string) => string | undefined) | undefined;
  history?: HistoryLike | undefined;
  location?: LocationLike | undefined;
  /** Where `<a data-route>` clicks and `popstate` are listened for. */
  events?: Pick<Window, "addEventListener" | "removeEventListener"> | undefined;
}

export interface Router {
  /** Shows the view for the current location. */
  start(): Promise<void>;
  navigate(path: string, options?: { replace?: boolean }): Promise<void>;
  /** The current path and query, e.g. `/device?code=BCDF-GHJK`. */
  current(): string;
  stop(): void;
}

export function createRouter(options: RouterOptions): Router {
  const history = options.history ?? window.history;
  const location = options.location ?? window.location;
  const events = options.events ?? window;
  let teardown: (() => void) | undefined;
  let rendering = 0;

  async function show(): Promise<void> {
    const ticket = ++rendering;
    teardown?.();
    teardown = undefined;

    let path = location.pathname;
    const redirect = options.guard?.(path, location.search);
    if (redirect !== undefined && redirect !== path + location.search) {
      history.replaceState(null, "", redirect);
      path = location.pathname;
    }

    const view = options.routes[path];
    if (!view) {
      history.replaceState(null, "", options.fallback);
      return show();
    }

    const result = await view({
      root: options.root,
      params: new URLSearchParams(location.search),
      navigate: (to, opts) => void router.navigate(to, opts),
    });
    if (typeof result === "function") {
      // A newer navigation started while this view was loading: tear this one down at once.
      if (ticket === rendering) {
        teardown = result;
      } else {
        result();
      }
    }
  }

  function onClick(event: Event): void {
    const mouse = event as MouseEvent;
    if (
      mouse.defaultPrevented ||
      mouse.button !== 0 ||
      mouse.metaKey ||
      mouse.ctrlKey ||
      mouse.shiftKey ||
      mouse.altKey
    ) {
      return;
    }

    const link = (event.target as Element | null)?.closest?.("a[data-route]");
    const href = link?.getAttribute("href");
    if (href?.startsWith("/")) {
      event.preventDefault();
      void router.navigate(href);
    }
  }

  const onPopState = () => void show();

  const router: Router = {
    async start() {
      events.addEventListener("click", onClick);
      events.addEventListener("popstate", onPopState);
      await show();
    },
    async navigate(path, opts) {
      if (opts?.replace) {
        history.replaceState(null, "", path);
      } else {
        history.pushState(null, "", path);
      }

      await show();
    },
    current: () => location.pathname + location.search,
    stop() {
      events.removeEventListener("click", onClick);
      events.removeEventListener("popstate", onPopState);
      teardown?.();
      teardown = undefined;
    },
  };

  return router;
}
