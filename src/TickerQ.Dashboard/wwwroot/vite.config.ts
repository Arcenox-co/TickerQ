import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ command }) => ({
  // Relative base so the pre-built bundle resolves its assets under whatever
  // path the host mounts the dashboard at (SetBasePath). The dashboard
  // middleware serves index.html with <base href="{basePath}/"> injected, so
  // emitted "./assets/..." URLs resolve to "{basePath}/assets/..." regardless
  // of mount point or route depth — no per-customer rebuild, no runtime
  // URL-rewriting plugin. Dev server stays at "/" for clean HMR.
  base: command === "build" ? "./" : "/",
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  // Dev workflow: run a local scheduler on :8080, then `npm run dev` here.
  // Vite proxies the dashboard's REST + SignalR calls so we don't hit CORS.
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:8080", changeOrigin: true },
      "/tickerq-notification-hub": {
        target: "http://localhost:8080",
        ws: true,
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: "dist",
    assetsDir: "assets",
    sourcemap: true,
  },
}));
