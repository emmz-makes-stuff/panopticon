import "@fontsource/gabarito/700.css";
import "@fontsource/atkinson-hyperlegible-next/400.css";
import "@fontsource/atkinson-hyperlegible-next/600.css";
import "./styles.css";
import { startShell } from "./shell";

// Session first (design D18): the shell asks who's signed in before any view loads change data.
const root = document.getElementById("app");
if (root) {
  void startShell(root);
}
