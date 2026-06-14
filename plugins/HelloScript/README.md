# HelloScript - sample plugin

A **JavaScript** plugin that registers four capabilities from one script and no build step: a save
operation, a CLI command, a menu action, and an event handler. It is the broadest single-file
example of the `abiotic` host API.

- **Runtime:** JavaScript (Jint), `plugin.js`
- **Capabilities:** save operation + console command + menu action + event handler

## What it does

| Capability | Id / name | Behavior |
|---|---|---|
| Save operation | `rich-player` | sets money and raises every skill to a target level (player saves) |
| Console command | `js-greet` | prints a greeting; takes an optional `--name` |
| Menu action | `say-hi` | shows a notification with the open save's path |
| Event handler | `save.written` | logs whenever a plugin operation writes a save |

## Manifest

```json
{
  "id": "com.abioticeditor.samples.hello-script",
  "name": "Hello Script",
  "version": "1.0.0",
  "author": "Abiotic Editor samples",
  "description": "A JavaScript plugin: a 'rich-player' save operation, a 'js-greet' command, a menu action, and a save.written event handler.",
  "runtime": "javascript",
  "entryScript": "plugin.js",
  "minHostVersion": "1.0.0",
  "capabilities": ["saveOperation", "consoleCommand", "menuAction", "eventHandler"],
  "enabled": true
}
```

A JavaScript plugin needs only these two files: a `plugin.json` with `"runtime": "javascript"` and
`"entryScript": "plugin.js"`, and the script itself.

## How it works

The script runs once at load and registers capabilities on the injected `abiotic` host object:

```javascript
abiotic.registerSaveOperation({ id: "rich-player", appliesTo: "Player", parameters: [...], execute: function (ctx) { ... } });
abiotic.registerCommand({ name: "js-greet", options: [...], invoke: function (ctx) { ... } });
abiotic.registerMenuAction({ id: "say-hi", title: "Say hello (JS)", glyph: "👋", invoke: function (ctx) { ... } });
abiotic.on("save.written", function (event) { abiotic.log.info("saw " + event.name + " for " + event.savePath); });
```

For player save operations, `ctx.player` is a focused facade: `ctx.player.money` (get/set),
`ctx.player.setAllSkillLevels(n)` (returns how many changed), `skillCount`, `recipeCount`. Editing
through it marks the context changed, so the host persists it through the same backup/write path as
a managed operation. Member access is case-insensitive, so natural camelCase works
(`ctx.markChanged()` maps to the CLR `MarkChanged`).

The Jint engine is pure-managed (so it runs on desktop, mobile, and CLI with no native dependency),
single-threaded, and bounded (recursion depth, a wall-clock timeout, a statement cap). Like managed
plugins, it runs in-process and is not a security boundary.

## Build

None. Copy the folder into a plugins directory (or point `ABIOTIC_PLUGINS_DIR` at it).

## Try it

```console
abioticeditor plugins run rich-player path\to\Player_<id>.sav --param money=50000 --param level=15
abioticeditor js-greet --name Scientist
```

In the app the menu action appears in the **Plugins** menu and the Plugins panel.
