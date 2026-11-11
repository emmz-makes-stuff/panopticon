import { ApiError, createApi, type Api, type FetchLike, type SessionMember } from "./api";
import { PRODUCT_NAME } from "./app";
import type { EventSourceFactory } from "./live";
import { el, routeLink } from "./dom";
import { DEFAULT_QUIET, isQuietSettings, type QuietSettings } from "./quiet";
import { safeStorage, saveReturnPath, takeReturnPath } from "./returnPath";
import {
  createRouter,
  type HistoryLike,
  type LocationLike,
  type Router,
  type View,
} from "./router";
import { cardsView } from "./views/cards";
import { changeView } from "./views/change";
import { deviceView } from "./views/device";
import { historyView } from "./views/history";
import { metricsView } from "./views/metrics";
import { membersView } from "./views/members";
import { projectView, projectsView } from "./views/projects";
import { confirmView, signInView } from "./views/signIn";
import { tokensView } from "./views/tokens";

/** Pages anyone can open. Everything else needs a session. */
export const PUBLIC_PATHS: readonly string[] = ["/sign-in", "/sign-in/confirm"];

export type StorageLike = Pick<Storage, "getItem" | "setItem" | "removeItem">;

/** What every view gets from the shell. */
export interface Env {
  api: Api;
  /** The signed-in member, or null. */
  member(): SessionMember | null;
  /** After a confirmed sign-in: shows the header and goes where the member was headed. */
  signedIn(member: SessionMember): void;
  /** After a rename: the header shows the new name. */
  renamed(displayName: string): void;
  /** Where the cards view shows the live status and change count. */
  statusSlot: HTMLElement;
  /**
   * When cards go idle and stale (`GET /api/settings`, filters-and-idle-cards D1), asked for once when the signed-in
   * shell starts. It never rejects: settings that fail to load, or don't make sense, give the defaults (with a
   * console warning), and the next call asks again.
   */
  quietSettings(): Promise<QuietSettings>;
  /**
   * Per-browser storage, shared by every tab: the address a sign-in link was asked for, and the return path (which
   * has to survive the emailed link opening in a new tab).
   */
  local: StorageLike;
  location: LocationLike & { readonly hash: string; readonly origin: string };
  history: HistoryLike;
  now: () => Date;
  createEventSource?: EventSourceFactory | undefined;
  /** Confirms a destructive action (removing a member, revoking a token). */
  confirm: (message: string) => boolean;
  /** Puts text on the clipboard (a Project's link file). Rejects when the browser won't allow it. */
  copy: (text: string) => Promise<void>;
  /** The visitor's OS as the browser names it (e.g. "macOS", "Windows", "MacIntel", "Win32"), for the install hint. */
  platform: () => string;
}

export interface ShellDeps {
  fetch?: FetchLike | undefined;
  createEventSource?: EventSourceFactory | undefined;
  local?: StorageLike | undefined;
  history?: HistoryLike | undefined;
  location?: Env["location"] | undefined;
  events?: Pick<Window, "addEventListener" | "removeEventListener"> | undefined;
  now?: (() => Date) | undefined;
  confirm?: ((message: string) => boolean) | undefined;
  copy?: ((text: string) => Promise<void>) | undefined;
  platform?: (() => string) | undefined;
}

export interface Shell {
  /** Null when the session check failed for a reason other than 401, so no view started. */
  router: Router | null;
  member(): SessionMember | null;
}

/**
 * The signed-in app (design D18). It asks `GET /api/session` before anything else, so nothing loads change data
 * for someone who isn't signed in. Signed out, every page but sign-in sends you to `/sign-in`, remembering where you
 * were going. Any 401 later, from any call, does the same.
 */
export async function startShell(root: HTMLElement, deps: ShellDeps = {}): Promise<Shell> {
  const history = deps.history ?? window.history;
  const location = deps.location ?? window.location;
  let member: SessionMember | null = null;
  let router: Router | undefined = undefined;

  const api = createApi({ fetch: deps.fetch, onSignedOut: () => signedOut() });

  const header = document.createElement("header");
  header.className = "top";
  const wordmark = document.createElement("a");
  wordmark.className = "wordmark";
  wordmark.href = "/";
  wordmark.dataset["route"] = "";
  wordmark.textContent = PRODUCT_NAME;
  const heading = document.createElement("h1");
  heading.className = "wordmark-heading";
  heading.append(wordmark);
  const statusSlot = document.createElement("div");
  statusSlot.className = "status-slot";
  const account = document.createElement("nav");
  account.className = "account";
  account.setAttribute("aria-label", "Account");
  header.append(heading, statusSlot, account);
  const view = document.createElement("div");
  view.className = "view";
  root.replaceChildren(header, view);

  function renderAccount(): void {
    if (!member) {
      account.replaceChildren();
      return;
    }

    const name = document.createElement("span");
    name.className = "account-name";
    // Display names are text, never markup (spec team-members, "Display names").
    name.textContent = member.displayName;
    const signOut = document.createElement("button");
    signOut.type = "button";
    signOut.className = "link-button";
    signOut.textContent = "Sign out";
    const failure = el("span", "account-error");
    failure.setAttribute("role", "alert");
    signOut.addEventListener("click", () => {
      failure.textContent = "";
      void api.send("POST", "/api/auth/sign-out").then(
        () => {
          member = null;
          renderAccount();
          void router?.navigate("/sign-in", { replace: true });
        },
        (error: unknown) => {
          if (error instanceof ApiError && error.status === 401) {
            return; // Already signed out: the 401 has sent us to sign in.
          }

          // Still signed in, so say so beside the button rather than fail silently.
          failure.textContent = "Couldn't sign out. Try again.";
        },
      );
    });
    account.replaceChildren(
      name,
      routeLink("/projects", "Projects"),
      routeLink("/members", "Members"),
      routeLink("/history", "History"),
      routeLink("/metrics", "Metrics"),
      routeLink("/tokens", "Tokens"),
      signOut,
      failure,
    );
  }

  function rememberReturn(path: string): void {
    if (!PUBLIC_PATHS.includes(new URL(path, "http://x").pathname)) {
      saveReturnPath(env.local, path, env.now());
    }
  }

  // Any 401 once the app is running: the session ended (signed out elsewhere, removed, or expired). Before the router
  // starts, the guard below does this job for the first page.
  function signedOut(): void {
    member = null;
    renderAccount();
    if (!router || PUBLIC_PATHS.includes(location.pathname)) {
      return;
    }

    rememberReturn(router.current());
    void router.navigate("/sign-in", { replace: true });
  }

  let quiet: Promise<QuietSettings> | undefined;
  function loadQuietSettings(): Promise<QuietSettings> {
    quiet ??= api.get<unknown>("/api/settings").then(
      (settings) => {
        if (isQuietSettings(settings)) {
          return settings;
        }

        console.warn(
          "The server's idle and stale settings don't make sense; using the defaults.",
          settings,
        );
        quiet = undefined;
        return DEFAULT_QUIET;
      },
      (error: unknown) => {
        console.warn("Couldn't load the idle and stale settings; using the defaults.", error);
        quiet = undefined;
        return DEFAULT_QUIET;
      },
    );
    return quiet;
  }

  const env: Env = {
    api,
    member: () => member,
    signedIn(next) {
      member = next;
      renderAccount();
      void router?.navigate(takeReturnPath(env.local, env.now(), location.origin) ?? "/", {
        replace: true,
      });
    },
    renamed(displayName) {
      if (member) {
        member = { ...member, displayName };
        renderAccount();
      }
    },
    statusSlot,
    quietSettings: loadQuietSettings,
    // Blocked storage (site data off) must not break sign-in: reads give null and writes are dropped.
    local: safeStorage(() => deps.local ?? window.localStorage),
    location,
    history,
    now: deps.now ?? (() => new Date()),
    createEventSource: deps.createEventSource,
    confirm: deps.confirm ?? ((message) => window.confirm(message)),
    copy: deps.copy ?? ((text) => navigator.clipboard.writeText(text)),
    platform: deps.platform ?? browserPlatform,
  };

  const routes: Record<string, View> = {
    "/": cardsView(env),
    "/sign-in": signInView(env),
    "/sign-in/confirm": confirmView(env),
    "/projects": projectsView(env),
    "/projects/:id": projectView(env),
    "/members": membersView(env),
    "/tokens": tokensView(env),
    "/device": deviceView(env),
    "/history": historyView(env),
    "/metrics": metricsView(env),
    "/changes/:id": changeView(env),
  };

  try {
    member = await api.get<SessionMember>("/api/session");
  } catch (error) {
    if (!(error instanceof ApiError && error.status === 401)) {
      // Not "signed out" but "can't tell": say so, rather than leave a blank page or guess at sign-in.
      view.replaceChildren(
        el(
          "section",
          "load-error",
          el("h2", undefined, "Couldn't reach the server"),
          el(
            "p",
            undefined,
            `${error instanceof Error ? error.message : String(error)}. Check that the server is running, then reload.`,
          ),
        ),
      );
      view.firstElementChild?.setAttribute("role", "alert");
      return { router: null, member: () => member };
    }
  }

  if (member) {
    void loadQuietSettings();
  }

  renderAccount();
  router = createRouter({
    root: view,
    routes,
    fallback: "/",
    guard(path, search) {
      if (!member && !PUBLIC_PATHS.includes(path)) {
        rememberReturn(path + search);
        return "/sign-in";
      }

      return member && path === "/sign-in" ? "/" : undefined;
    },
    history,
    location,
    events: deps.events,
  });
  await router.start();
  return { router, member: () => member };
}

/** The browser's name for the OS: User-Agent Client Hints where there are some, else the older `navigator.platform`. */
function browserPlatform(): string {
  const hints = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData;
  return hints?.platform || navigator.platform || "";
}
