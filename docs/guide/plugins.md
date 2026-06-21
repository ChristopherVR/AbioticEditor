# Plugins & language packs

The editor can be extended with **plugins**: small add-ons that bolt new abilities onto the app and
the CLI without rebuilding either. A plugin can add a one-click **save operation** (a cheat, a bulk
edit, or a repair for something a game patch changed), a new **CLI command**, an in-app **panel**, or
a **language pack** that translates the UI. This page is about *finding, installing, and running*
plugins. If you want to *write* one, see the [plugin development reference](/reference/plugin-system).

::: warning Plugins run with full trust
A plugin runs with the same access to your computer as the editor itself - there is no sandbox. Only
install plugins from authors you trust. The editor makes loading deliberate (you drop a folder in and
it's listed with where it came from, and you can disable it), but it cannot contain a malicious
plugin. Your saves are still safe-by-construction: every plugin write keeps a `.bak`.
:::

## Installing a plugin

A plugin is just a **folder** containing a `plugin.json` file plus its code (`.dll` or `.js`) and any
assets. To install one, drop that folder into your plugins directory:

```
%LOCALAPPDATA%\AbioticEditor\plugins\<the-plugin-folder>\
```

Restart the editor (or the CLI picks it up on the next run) and it's available. Plugins installed here
survive app updates.

## Managing plugins in the app

Open **Settings ▸ Plugins ▸ Manage Plugins**. Each installed plugin is listed with its name, author,
where it came from, and whether it loaded. From here you can:

- **Enable or disable** a plugin (disabled plugins are listed but never loaded).
- **Run a save operation** against the currently open save - the editor keeps a `.bak` and reloads
  the save afterward.
- **Open a tool panel** that a plugin provides.

Menu actions a plugin registers also appear in the top-level **Plugins** menu.

## Using plugins from the CLI

```console
abioticeditor plugins list                       # installed plugins, load state, capabilities
abioticeditor plugins info <id>                  # one plugin's details
abioticeditor plugins run <operation> <save>     # run a save operation (keeps a .bak)
abioticeditor plugins run <operation> <save> --param name=value --dry-run
abioticeditor <plugin-command> ...               # a plugin's CLI commands are top-level verbs
```

`--dry-run` previews a save operation without writing anything. A plugin's own console commands
behave exactly like built-in verbs and show up in `abioticeditor --help`.

## Language packs are plugins

A translation of the editor's UI ships as a plugin too - often a **pure-data pack** with no code at
all, just a manifest and a `.json` or `.resx` file of translated strings. Install it the same way
(drop the folder into the plugins directory) and pick the language under **Settings ▸ LANGUAGE**. A
pack can add a whole new language or just override the strings it wants; anything it omits falls back
to English.

Want to translate the editor yourself? See [Localization](/reference/localization).

## Writing your own

Plugins can be compiled .NET assemblies or plain JavaScript files (no build step). The full SDK,
the capability contracts, worked samples, and a fix-up cookbook live in the technical reference:

- [Plugin system](/reference/plugin-system) - the architecture and the *why*.
- [Authoring guide](/reference/plugin-authoring) - step-by-step, every capability kind.
- [Building & installing](/reference/plugin-building) - the practical mechanics.
- [Sample catalog](/reference/plugin-samples) - eleven worked examples to copy from.
- [Fix-up cookbook](/reference/plugin-fixups) - recipes for repairing saves over time.
