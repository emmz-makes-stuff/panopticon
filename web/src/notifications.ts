import type { HeldPrompt } from "./sessionRows";

/**
 * Opt-in browser notifications for held prompts (spec live-dashboard, Browser notifications; agent-interaction D10).
 * The browser is asked for permission only when the member chooses to turn them on: never on load, never on a
 * prompt. Whether they're on is kept per browser, and a storage failure only means they're off.
 */

/** The parts of the browser's Notification API used here, so tests can supply a fake. */
export interface NotificationHost {
  /** "unsupported" where the browser has no notifications. */
  permission(): NotificationPermission | "unsupported";
  requestPermission(): Promise<NotificationPermission>;
  show(title: string, options: NotificationOptions, onClick: () => void): void;
}

export type StorageLike = Pick<Storage, "getItem" | "setItem" | "removeItem">;

/** The storage key: "on" when the member has turned notifications on in this browser. */
export const NOTIFICATIONS_KEY = "panopticon.notifications";

export type EnableResult = "on" | "denied" | "unsupported";

export interface Notifier {
  /** Whether the browser has notifications at all. */
  supported(): boolean;
  /** On: chosen in this browser, and the browser allows them. */
  on(): boolean;
  /** Turns them on, asking the browser's permission (the one place that's asked). */
  enable(): Promise<EnableResult>;
  disable(): void;
  /**
   * The held prompts the viewer may answer, as they are now. The first call only notes them (they're on the screen
   * already); after that, one notification for each prompt not seen before, while notifications are on.
   */
  held(prompts: readonly HeldPrompt[]): void;
}

/** The browser's notifications, or a host that says they're unsupported. */
export function browserNotifications(): NotificationHost {
  const api = typeof Notification === "undefined" ? undefined : Notification;
  return {
    permission: () => api?.permission ?? "unsupported",
    requestPermission: () => api?.requestPermission() ?? Promise.resolve("denied"),
    show(title, options, onClick) {
      if (!api) {
        return;
      }

      const notification = new api(title, options);
      notification.onclick = () => {
        window.focus();
        notification.close();
        onClick();
      };
    },
  };
}

/** `focus(changeId)` brings the dashboard to the card a notification is about. */
export function createNotifier(
  host: NotificationHost,
  storage: StorageLike,
  focus: (changeId: string) => void,
): Notifier {
  const seen = new Set<string>();
  let primed = false;
  // This page's own choice, which holds even when storage is blocked.
  let choice: boolean | undefined;

  const chosen = () => {
    if (choice !== undefined) {
      return choice;
    }

    try {
      return storage.getItem(NOTIFICATIONS_KEY) === "on";
    } catch {
      return false;
    }
  };

  const notifier: Notifier = {
    supported: () => host.permission() !== "unsupported",
    on: () => chosen() && host.permission() === "granted",
    async enable() {
      if (!notifier.supported()) {
        return "unsupported";
      }

      const permission =
        host.permission() === "granted" ? "granted" : await host.requestPermission();
      if (permission !== "granted") {
        return "denied";
      }

      choice = true;
      try {
        storage.setItem(NOTIFICATIONS_KEY, "on");
      } catch {
        // Blocked storage: on for this page only.
      }

      return "on";
    },
    disable() {
      choice = false;
      try {
        storage.removeItem(NOTIFICATIONS_KEY);
      } catch {
        // Nothing stored, so nothing to clear.
      }
    },
    held(prompts) {
      const fresh = prompts.filter((prompt) => !seen.has(prompt.promptId));
      for (const prompt of fresh) {
        seen.add(prompt.promptId);
      }

      if (!primed) {
        primed = true;
        return;
      }

      if (!notifier.on()) {
        return;
      }

      for (const prompt of fresh) {
        host.show(
          `Needs you: ${prompt.change}`,
          {
            body:
              prompt.kind === "question"
                ? "A question is waiting for you."
                : prompt.kind === "visit"
                  ? "A visit request is waiting for you."
                  : `Permission for ${prompt.tool ?? "a tool"} is waiting for you.`,
            // One per prompt, even with the dashboard open in several tabs.
            tag: prompt.promptId,
          },
          () => focus(prompt.changeId),
        );
      }
    },
  };

  return notifier;
}
