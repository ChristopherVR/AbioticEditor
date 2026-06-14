// Web Stats: a JavaScript plugin with an OFFLINE HTML web tool.
//
// Unlike ReactDashboard (inline HTML pulling React from a CDN), this one serves a bundled
// folder of static assets: no internet, no build step, no framework. It shows the
// directory-served path: `rootDirectory` is resolved against the plugin's own folder, so the
// page lives in ./web/index.html and talks to the plugin through the same host bridge.

abiotic.registerWebTool({
    id: "web-stats",
    title: "Web Stats (offline)",
    glyph: "🌐",
    rootDirectory: "web",
    entryFile: "index.html",
    handleMessage: function (message, ctx) {
        var req;
        try { req = JSON.parse(message); } catch (e) { req = {}; }
        if (req.type === "playerSummary") {
            return ctx.playerSummaryJson();
        }
        return JSON.stringify({ error: "unknown request" });
    }
});
