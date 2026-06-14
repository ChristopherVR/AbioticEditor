# ReactDashboard - sample plugin

A **JavaScript** plugin whose UI is a **React app rendered from inline HTML**, with React, ReactDOM,
and Babel pulled from a CDN. No build step: the whole UI is a single HTML string in `plugin.js`. It
is the middle point between [`WebStats`](../WebStats/) (plain offline HTML) and
[`ReactAppDashboard`](../ReactAppDashboard/) (a full Vite build).

- **Runtime:** JavaScript (Jint), `plugin.js`
- **Capabilities:** web tool `react-dashboard` + event handler
- **Surfaced in:** the app's Plugins panel (web view)

## What it does

Registers a web tool that renders a React dashboard. The page fetches the open player save's summary
through the host bridge and shows money, skill count, top level, recipes, and a skill list, with a
refresh button. It also listens for host events so it can refresh when the open save changes.

## Manifest

```json
{
  "id": "com.abioticeditor.samples.react-dashboard",
  "name": "React Dashboard",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "A JavaScript plugin whose UI is a React app rendered in a web view; it reads the open player save through the host bridge.",
  "runtime": "javascript",
  "entryScript": "plugin.js",
  "minHostVersion": "1.0.0",
  "capabilities": ["webTool", "eventHandler"],
  "enabled": true
}
```

## How it works

The script holds the page as an HTML string that loads React and Babel from `unpkg`, then registers
it as inline web-tool content:

```javascript
abiotic.registerWebTool({
    id: "react-dashboard", title: "React Dashboard", glyph: "⚛️",
    html: HTML,
    handleMessage: function (message, ctx) {
        var req = JSON.parse(message);
        if (req.type === "playerSummary") return ctx.playerSummaryJson();
        return "{}";
    }
});
```

Inside the page, a React component does
`abiotic.request({ type: "playerSummary" }).then(setData)` and re-renders. `abiotic.onEvent(fn)`
lets it refresh when the host pushes an event.

> **CDN caveat.** Loading React from a CDN needs internet at runtime. For a plugin you intend to
> ship, bundle the assets locally (see `WebStats` or `ReactAppDashboard`) or pin Subresource
> Integrity hashes. This sample uses a CDN only to stay build-free and short.

## Build

None. Copy the folder into a plugins directory.

## Try it

Install it, then in the app: Settings, Manage Plugins, WEB TOOLS, open **React Dashboard** with a
player save open. Requires an internet connection (CDN).
