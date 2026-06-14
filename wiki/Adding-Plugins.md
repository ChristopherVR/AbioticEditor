# Adding Plugins

This page is for **using** plugins: installing them, turning them on and off, and running them. To
write your own, see [Building Plugins](Building-Plugins).

> **Trust first.** Plugins run with full trust. There is no sandbox; a loaded plugin can do anything
> the editor can. Only install plugins from a source you trust.

## What a plugin looks like

A plugin is a **folder** containing a `plugin.json` manifest next to its code (`.dll` for a .NET
plugin, `.js` for a JavaScript one), plus any assets it ships:

```
max-skills/
  plugin.json
  MaxSkills.dll

hello-script/
  plugin.json
  plugin.js
```

## Where to put it

The editor scans two roots, **user first** (a user-installed plugin shadows a bundled one with the
same id):

| Root | Path | For |
|---|---|---|
| User | `%LOCALAPPDATA%\AbioticEditor\plugins` | plugins you install; survive app updates |
| Bundled | folder next to the app/CLI executable, `\plugins` | plugins shipped with the editor |

To install a plugin, copy its folder into the user root:

```
%LOCALAPPDATA%\AbioticEditor\plugins\max-skills\
```

Restart the app (or re-run the CLI) and the plugin is discovered.

### Pointing at a different folder

Set the `ABIOTIC_PLUGINS_DIR` environment variable to one or more folders (joined by the platform
path separator) to override both roots. This is handy for trying a plugin without copying it:

```powershell
$env:ABIOTIC_PLUGINS_DIR = "D:\downloads\some-plugin"
abioticeditor plugins list
```

Set `ABIOTIC_NO_PLUGINS=1` to disable all plugin loading.

## Using plugins in the app

Open **Settings**, then **Manage Plugins**. There you can:

- see every installed plugin with its **load state** and where it came from;
- **enable or disable** each plugin (the choice is saved to its `plugin.json`; restart to apply);
- **run save operations** against the open save (the editor keeps a `.bak` and reloads afterward);
- open **editor tools** and **web tools** (UI panels);
- run **menu actions** (these also appear in the top-level **Plugins** menu).

A plugin that failed to load is listed with its error, and the editor's built-in features keep
working regardless.

## Using plugins in the CLI

```console
abioticeditor plugins list                       # installed plugins, load state, capabilities
abioticeditor plugins info <id>                  # one plugin's operations, commands, tools, errors
abioticeditor plugins run <operation> <save>     # run a save operation (keeps a .bak)
abioticeditor plugins run <operation> <save> --param name=value
abioticeditor plugins run <operation> <save> --dry-run    # preview, write nothing
abioticeditor <plugin-command> ...               # console commands are top-level verbs
```

Plugin **console commands** show up in `abioticeditor --help` and run like built-in verbs. The CLI
skips loading plugins that only provide UI (it cannot host a UI), and `ABIOTIC_NO_PLUGINS=1`
disables loading entirely.

## Safety

- Every save operation keeps a **`.bak`** of the previous file before it writes. A `--dry-run` never
  touches the file.
- A plugin that throws on load or while running is **reported, not fatal**. The editor stays up.
- **Disable** anything you do not trust or are not using. Disabled plugins are listed but never
  loaded.

## Troubleshooting

- **Not listed:** check the folder is directly under a plugin root and contains a valid
  `plugin.json`. Run `abioticeditor plugins list` to confirm discovery.
- **Listed as Failed:** run `abioticeditor plugins info <id>` to read the load error. A common cause
  is a plugin built against a newer SDK than your editor (`minHostVersion` exceeds the host).
- **A `.dll` plugin will not load:** make sure the folder has only the plugin's own DLL plus
  `plugin.json`. A plugin should not ship copies of the editor's shared assemblies.
