// React App Dashboard: the JavaScript glue for a full Vite + React plugin UI.
//
// The UI is a real Vite + React app under ./app, built to a single self-contained file at
// ./app/dist/index.html. This script:
//   1. registers a web tool that serves that built app, and
//   2. registers a save operation the app can trigger, and
//   3. answers the app's bridge requests (read save data, run the op, raise a toast).
//
// Build the app once with:  cd app && npm install && npm run build

var SKILL_OP_ID = "react-max-skills";

// A save operation the React UI triggers (via abiotic.ui.runSaveOperation through the bridge).
abiotic.registerSaveOperation({
    id: SKILL_OP_ID,
    displayName: "Max Skills (React sample)",
    appliesTo: "Player",
    execute: function (ctx) {
        var raised = ctx.player.setAllSkillLevels(20);
        return { message: "raised " + raised + " skills to level 20", changeCount: raised };
    }
});

abiotic.registerWebTool({
    id: "react-app-dashboard",
    title: "React App",
    glyph: "⚛️",
    rootDirectory: "app/dist",   // resolved against this plugin's folder; serves index.html
    entryFile: "index.html",
    handleMessage: function (message, ctx) {
        var req;
        try { req = JSON.parse(message); } catch (e) { req = {}; }

        if (req.type === "playerSummary") {
            return ctx.playerSummaryJson();
        }
        if (req.type === "toast") {
            // The web UI drives the APP: raise a native toast.
            abiotic.ui.toast(req.message || "Hello from the React app");
            return JSON.stringify({ ok: true });
        }
        if (req.type === "runOperation") {
            // The web UI drives the APP: run a registered save operation through the host's
            // backup/write path, which also reloads the editor.
            abiotic.ui.runSaveOperation(req.operationId || SKILL_OP_ID);
            return JSON.stringify({ wrote: true });
        }
        return JSON.stringify({ error: "unknown request: " + (req.type || "") });
    }
});

abiotic.on("save.opened", function (e) {
    abiotic.log.info("react-app-dashboard: save opened " + e.savePath);
});
