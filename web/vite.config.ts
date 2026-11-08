import { defineConfig } from "vitest/config";

// The server listens on 5080 in development: macOS ControlCenter holds port 5000.
const serverUrl = process.env["PANOPTICON_DEV_SERVER"] ?? "http://localhost:5080";

export default defineConfig({
  build: {
    // Design D10: the server serves the built UI from its wwwroot.
    outDir: "../src/Panopticon.Server/wwwroot",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/api": serverUrl,
      "/mcp": serverUrl,
    },
  },
  test: {
    environment: "happy-dom",
  },
});
