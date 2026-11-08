/** Mounts the dashboard into `root`. Section 7 replaces this placeholder with the card list. */
export function mountApp(root: HTMLElement): void {
  const heading = document.createElement("h1");
  heading.textContent = "Panopticon";
  root.replaceChildren(heading);
}
