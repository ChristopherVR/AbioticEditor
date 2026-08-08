---
layout: home

hero:
  name: Abiotic Editor
  text: Save editing for Abiotic Factor
  tagline: Edit your save in the browser with nothing to install, or download the desktop app. Same editor, same engine.
  image:
    src: /logo.png
    alt: Abiotic Editor
  actions:
    - theme: brand
      text: Edit your save in the browser
      link: /app/
      target: _self
    - theme: alt
      text: Download the app
      link: https://github.com/ChristopherVR/AbioticEditor/releases/latest
    - theme: alt
      text: Get started
      link: /guide/getting-started

features:
  - title: Runs in your browser, nothing to install
    details: Open the editor, point it at your save folder, and edit. Your saves never leave your computer - there is no server to send them to.
    link: /guide/browser-editor
    linkText: How to open your saves
  - title: Edits the whole save
    details: Reads and writes the game's GVAS saves. Anything the editor doesn't touch is left as-is, and every save keeps a .bak.
  - title: Knows the game's data
    details: Mounts the installed game's pak archives to resolve items, recipes, skills, quest flags, fish, and traits. Still works when the game isn't installed.
  - title: Desktop app and CLI, one engine
    details: The desktop editor and command-line tools share the same save engine, so they produce the same reliable output.
  - title: Extensible with plugins
    details: Add save operations, CLI verbs, and UI panels as .NET assemblies or plain JavaScript. Scripts need no build step.
---

## Edit your save right now, in this browser

No download, no install. **[Open the editor](/app/)** and you are three clicks away:

1. Press **OPEN FOLDER**.
2. Choose your **account** folder - the one holding `Worlds/`, named after your SteamID:
   `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<your SteamID>` on Windows. Picking the
   account folder rather than a single world lists every world together and lets you edit your
   character's look, which is stored beside `Worlds/` rather than inside it.
3. Allow the browser to view the folder when it asks, then pick a save and edit.

Your saves never leave your computer. There is no server behind this: the editor runs inside
the page and reads the folder you chose straight off your disk.

::: tip Chrome and Edge can save directly; Firefox and Safari cannot
In Chrome or Edge the editor writes changes back to your save folder and keeps a `.bak`, just
like the desktop app. Firefox and Safari do not let any web page write to your disk, so there
your changes live in the tab until you press **EXPORT** to download them. The editor tells you
which one you are in.
:::

Full walkthrough, including what the browser cannot do and how to get your saves back out:
**[Edit in your browser](/guide/browser-editor)**.

## What it edits

Point the editor at a save folder and edit player saves (vitals, inventory, equipment,
skills, traits, recipes, codex, transmog, spawn point, achievements, SteamID), world saves
(containers, quest flags, doors, dropped items, NPCs, bases, story progression), and the
story metadata save. Edits stage until you save, and quest-flag changes are gated by story
prerequisites so you can't create an inconsistent narrative state.

![The editor with a save folder loaded](/screenshots/01-loaded.png)

## See it in action

Everything is a friendly control over the real save data, with the game's own item icons,
recipe names, and quest text pulled straight from your installed copy of the game.

| | |
|---|---|
| ![Inventory](/screenshots/11-player-inventory.png) | ![Skills](/screenshots/12-player-skills.png) |
| **Inventory**: every slot, real icons, a full item catalogue. | **Skills**: levels, XP, and milestone perks for all fifteen skills. |
| ![Recipes](/screenshots/13-player-recipes.png) | ![Quest flags](/screenshots/21-world-questflags.png) |
| **Recipes**: your whole crafting book, searchable and filterable. | **Quest flags**: story progress, grouped by chapter, prerequisite-safe. |

There's a full walkthrough with screenshots of every screen in the
**[Desktop app guide](/guide/desktop-app)**.

Not sure where to begin? Head to **[Getting started](/guide/getting-started)**.

Building a plugin, translating the UI, or curious how the saves work? That's the
**[technical reference](/reference/save-format)**.
