# WebStats - sample plugin

A **JavaScript** plugin whose UI is an **offline HTML page** served from a bundled folder. No CDN,
no build step. It is the lightweight counterpart to [`ReactAppDashboard`](../ReactAppDashboard/)
(a full Vite build) and [`ReactDashboard`](../ReactDashboard/) (React from a CDN).

- **Runtime:** JavaScript (Jint), `plugin.js`
- **Capability:** web tool `web-stats`
- **Surfaced in:** the app's Plugins panel (web view)

## What it does

Registers a web tool that serves a static `web/index.html`. The page reads the open save through the
host bridge and renders a small stats view. Because the HTML and its assets are bundled in the
plugin folder, it works with no internet access.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.web-stats",
  "name": "Web Stats",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "A JavaScript plugin with an offline HTML web tool (no CDN) served from a bundled folder, reading the save via the host bridge.",
  "runtime": "javascript",
  "entryScript": "plugin.js",
  "minHostVersion": "1.0.0",
  "capabilities": ["webTool"],
  "enabled": true
}
```

## Layout

```
WebStats/
  plugin.json
  plugin.js          # registers the web tool, handles bridge messages
  web/
    index.html       # the offline UI (served directly)
```

## How it works

The script registers a web tool that points at a directory rather than inline HTML:

```javascript
abiotic.registerWebTool({
    id: "web-stats", title: "Web Stats (offline)", glyph: "🌐",
    rootDirectory: "web",        // relative path, resolved against the plugin folder
    entryFile: "index.html",
    handleMessage: function (message, ctx) {
        var req = JSON.parse(message);
        if (req.type === "playerSummary") return ctx.playerSummaryJson();
        return JSON.stringify({ error: "unknown request" });
    }
});
```

The host renders `web/index.html` in a `WebView` and injects a small bridge so page JavaScript can
call back into the plugin:

- `abiotic.request(obj)` returns a Promise that your `handleMessage(message, ctx)` resolves (the
  message is the JSON of `obj`); return a string, usually JSON.
- `abiotic.log(msg)` and `abiotic.onEvent(fn)` are also available.

In `handleMessage`, `ctx` exposes `activeSavePath`, `activeSaveKind`, and a ready-made
`ctx.playerSummaryJson()`.

> To **edit** from a web tool, have the page ask the host to run an `ISaveOperation` (so the write
> keeps its backup) rather than mutating the save from the page. See `ReactAppDashboard` for that.

## Build

None. Copy the folder (including `web/`) into a plugins directory.

## Try it

Install it, then in the app: Settings, Manage Plugins, WEB TOOLS, open **Web Stats (offline)** with
a player save open.
