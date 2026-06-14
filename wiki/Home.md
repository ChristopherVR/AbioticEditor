# Abiotic Editor wiki

Abiotic Editor is a save-game editor for **Abiotic Factor**. It reads and writes the game's GVAS
save files byte-for-byte and ships a desktop app and a command-line tool over one shared engine.

This wiki focuses on **plugins**: how to install them and how to build your own.

## Plugins in one minute

A plugin extends the editor without being part of its source tree. It is either a compiled **.NET
assembly** (`.dll`) or a **JavaScript file** (`.js`, no build step), and either kind can add:

- **save operations** (scripted edits over a save),
- **console commands** (new CLI verbs),
- **editor tools** (native UI panels),
- **web tools** (HTML/React UIs in a web view),
- **menu actions** (click-to-run items),
- **event handlers** (run when a save opens or is written),
- **save upgraders** (recover a save the editor cannot load yet).

Every plugin is a folder with a `plugin.json` manifest next to its `.dll` or `.js`.

> **Trust:** plugins run with full trust. There is no sandbox; a loaded plugin has the same
> privileges as the editor. The system makes loading deliberate and visible, it does not contain
> hostile code. Only install plugins you trust.

## Where to go next

- **[Adding Plugins](Adding-Plugins)** - install, enable/disable, and run plugins as a user.
- **[Building Plugins](Building-Plugins)** - write and build your own, managed or JavaScript.
- **[Plugin API Reference](Plugin-API-Reference)** - a quick reference of the SDK surface.

For the full, versioned docs see the [documentation
site](https://christophervr.github.io/AbioticEditor/plugins) and the
[`plugins/`](https://github.com/ChristopherVR/AbioticEditor/tree/main/plugins) sample folder.
