// Hello Script — a JavaScript plugin for the Abiotic Editor.
//
// JS plugins are first-class: they register the same capabilities managed (.dll) plugins do,
// through the `abiotic` host object. The script runs once at load; registration happens as a
// side effect of these calls. Authoring is plain ECMAScript on the bundled Jint engine — no
// build step, just a .js file next to a plugin.json with "runtime": "javascript".

abiotic.log.info("hello-script loading on host '" + abiotic.hostKind + "'");

// --- A save operation: make a player rich and skilled in one pass. -------------------------
abiotic.registerSaveOperation({
    id: "rich-player",
    displayName: "Rich & Skilled",
    description: "Set money and raise every skill to a target level (player saves).",
    appliesTo: "Player",
    parameters: [
        { name: "money", description: "Money to set (default 9999)", default: "9999" },
        { name: "level", description: "Skill level 1-20 (default 12)", default: "12" }
    ],
    execute: function (ctx) {
        // ctx.player is a typed helper for player saves; mutations go through the host's
        // backup/write path because each one marks the context changed.
        var money = parseInt(ctx.getParameter("money", "9999"), 10);
        var level = parseInt(ctx.getParameter("level", "12"), 10);

        ctx.player.money = money;
        var raised = ctx.player.setAllSkillLevels(level);

        return {
            message: "set money to " + money + " and raised " + raised + " skill(s) to level " + level,
            changeCount: raised + 1
        };
    }
});

// --- A console command: abioticeditor js-greet [--name X] -----------------------------------
abiotic.registerCommand({
    name: "js-greet",
    description: "Print a greeting from JavaScript.",
    options: [
        { name: "name", description: "Who to greet", isFlag: false }
    ],
    invoke: function (ctx) {
        var who = ctx.getOption("name", "scientist");
        ctx.print("Hello, " + who + " — from a JavaScript plugin.");
        return 0; // exit code
    }
});

// --- A menu action (surfaced in the GUI Plugins menu). -------------------------------------
abiotic.registerMenuAction({
    id: "say-hi",
    title: "Say hello (JS)",
    glyph: "👋",
    invoke: function (ctx) {
        var where = ctx.activeSavePath ? ctx.activeSavePath : "(no save open)";
        ctx.notify("Hello from JavaScript! Open save: " + where);
    }
});

// --- An event handler: react when any save is written. -------------------------------------
abiotic.on("save.written", function (event) {
    abiotic.log.info("hello-script saw " + event.name + " for " + event.savePath);
});

abiotic.log.info("hello-script loaded");
