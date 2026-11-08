import { ApiError, createApi, type Api, type FetchLike, type SessionMember } from "./api";
import { PRODUCT_NAME } from "./app";
import type { EventSourceFactory } from "./live";
import { el, routeLink } from "./dom";
import {
  createRouter,
  type HistoryLike,
  type LocationLike,
  type Router,
  type View,
} from "./router";
import { cardsView } from "./views/cards";
import { deviceView } from "./views/device";
import { membersView } from "./views/members";
import { confirmView, signInView } from "./views/signIn";
import { tokensView } from "./views/tokens";

/** Where the app goes after sign-in: the page you were sent away from (design D18 and D12's `/device?code=`). */
export const RETURN_TO_KEY = "panopticon.returnTo";

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
  /** Per-tab storage, for the return path (it survives the magic-link round trip in the same tab). */
  session: StorageLike;
  /** Per-browser storage, for the address a sign-in link was asked for. */
  local: StorageLike;
  location: LocationLike & { readonly hash: string; readonly origin: string };
  history: HistoryLike;
  now: () => Date;
  createEventSource?: EventSourceFactory | undefined;
  /** Confirms a destructive action (removing a member, revoking a token). */
  confirm: (message: string) => boolean;
}

export interface ShellDeps {
  fetch?: FetchLike | undefined;
  createEventSource?: EventSourceFactory | undefined;
  session?: StorageLike | undefined;
  local?: StorageLike | undefined;
  history?: HistoryLike | undefined;
  location?: Env["location"] | undefined;
  events?: Pick<Window, "addEventListener" | "removeEventListener"> | undefined;
  now?: (() => Date) | undefined;
  confirm?: ((message: string) => boolean) | undefined;
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
      routeLink("/members", "Members"),
      routeLink("/tokens", "Tokens"),
      signOut,
      failure,
    );
  }

  function rememberReturn(path: string): void {
    if (!PUBLIC_PATHS.includes(new URL(path, "http://x").pathname)) {
      env.session.setItem(RETURN_TO_KEY, path);
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

  const env: Env = {
    api,
    member: () => member,
    signedIn(next) {
      member = next;
      renderAccount();
      const returnTo = env.session.getItem(RETURN_TO_KEY);
      env.session.removeItem(RETURN_TO_KEY);
      void router?.navigate(
        returnTo?.startsWith("/") && !returnTo.startsWith("//") ? returnTo : "/",
        { replace: true },
      );
    },
    renamed(displayName) {
      if (member) {
        member = { ...member, displayName };
        renderAccount();
      }
    },
    statusSlot,
    session: deps.session ?? window.sessionStorage,
    local: deps.local ?? window.localStorage,
    location,
    history,
    now: deps.now ?? (() => new Date()),
    createEventSource: deps.createEventSource,
    confirm: deps.confirm ?? ((message) => window.confirm(message)),
  };

  const routes: Record<string, View> = {
    "/": cardsView(env),
    "/sign-in": signInView(env),
    "/sign-in/confirm": confirmView(env),
    "/members": membersView(env),
    "/tokens": tokensView(env),
    "/device": deviceView(env),
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
