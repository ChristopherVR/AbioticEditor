# React App Dashboard (sample plugin)

A **full Vite + React application** that runs as an Abiotic Editor plugin web tool. Unlike the
`ReactDashboard` sample (a single inline HTML string pulling React from a CDN), this is a real
front-end project with `package.json`, Vite, JSX, components, and a build step, exactly how
you'd ship a production plugin UI.

## Layout

```
ReactAppDashboard/
  plugin.json        # runtime: javascript, capabilities: webTool + saveOperation + eventHandler
  plugin.js          # registers the web tool (serves app/dist) + a save op + bridge handler
  app/               # the Vite + React project
    package.json
    vite.config.js   # base: "./" + vite-plugin-singlefile (inlines to one index.html)
    index.html
    src/
      main.jsx
      App.jsx        # the dashboard UI
      abiotic.js     # tiny wrapper around the host bridge (window.abiotic)
      index.css
    dist/            # build output: a single self-contained index.html (served by the plugin)
```

## Build

```bash
cd app
npm install
npm run build      # produces app/dist/index.html (one self-contained file)
```

The plugin's web tool points at `app/dist` (`rootDirectory`), so once built it runs with no
further steps. `vite-plugin-singlefile` inlines all JS/CSS into one `index.html` and
`base: "./"` keeps paths relative; both are needed so the page works when the editor serves it
from a `file://` URL inside a WebView (ES-module `<script>` requests would otherwise be blocked
by the `file://` CORS policy).

## What it demonstrates

- A real React app (Vite, JSX, hooks, components) as a plugin UI.
- Reading the open save: the app calls `abiotic.request({type:"playerSummary"})` and the plugin
  answers with the host's save summary.
- **Driving the app from the web UI** via the host-UI bridge:
  - *Max skills* → `abiotic.request({type:"runOperation"})` → the plugin calls
    `abiotic.ui.runSaveOperation("react-max-skills")`, which runs a real save operation through
    the editor's backup/write path and reloads the editor.
  - *Toast in app* → `abiotic.request({type:"toast"})` → the plugin calls `abiotic.ui.toast(...)`.

## Install

Copy `plugin.json`, `plugin.js`, and the built `app/dist/` into a folder under
`%LOCALAPPDATA%\AbioticEditor\plugins\react-app-dashboard\` (keeping `app/dist/index.html`'s
relative path), or point `ABIOTIC_PLUGINS_DIR` at this folder during development. Open it from
**Settings → Manage Plugins → WEB TOOLS → React App**.
