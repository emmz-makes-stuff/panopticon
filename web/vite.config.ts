import { defineConfig } from "vitest/config";

// The server listens on 7480 in development (5000 is macOS ControlCenter; 5080-5280 are commonly taken).
const serverUrl = process.env["PANOPTICON_DEV_SERVER"] ?? "http://localhost:7480";

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
