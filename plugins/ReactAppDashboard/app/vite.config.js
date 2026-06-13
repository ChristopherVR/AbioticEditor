import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";

// The build is served by the editor from a file:// URL inside a WebView. Two things make that
// work reliably across platforms:
//   * base: "./"            -> relative asset URLs (no leading "/").
//   * viteSingleFile()      -> inlines all JS/CSS into one index.html, so there are no ES-module
//                              <script> requests that a file:// origin would block via CORS.
// The result is a single self-contained dist/index.html the plugin points its web tool at.
export default defineConfig({
  base: "./",
  plugins: [react(), viteSingleFile()],
  build: {
    outDir: "dist",
    assetsInlineLimit: 100000000,
    cssCodeSplit: false,
  },
});
