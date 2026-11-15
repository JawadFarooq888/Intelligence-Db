import { fileURLToPath, URL } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// In development the SPA calls /api on its own origin and Vite forwards it to the API,
// so cookies stay same-origin and no CORS is needed.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  server: {
    port: 5173,
    host: true,
    // Codespaces serves the dev server on *.app.github.dev.
    allowedHosts: [".app.github.dev", "localhost"],
    proxy: {
      "/api": {
        target: process.env.IDB_API_URL ?? "http://localhost:5080",
        xfwd: true,
      },
      "/health": {
        target: process.env.IDB_API_URL ?? "http://localhost:5080",
      },
    },
  },
  build: {
    outDir: "dist",
    chunkSizeWarningLimit: 1500,
  },
});
