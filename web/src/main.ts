import "@fontsource/gabarito/700.css";
import "@fontsource/atkinson-hyperlegible-next/400.css";
import "@fontsource/atkinson-hyperlegible-next/600.css";
import "./styles.css";
import { mountApp } from "./app";
import { connectLive } from "./live";

const root = document.getElementById("app");
if (root) {
  void mountApp(root).then((app) => {
    connectLive(app);
    setInterval(() => app.updateTimes(), 30_000);
  });
}
