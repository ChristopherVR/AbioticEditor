---
layout: home

hero:
  name: Abiotic Editor
  text: Save editing for Abiotic Factor
  tagline: A desktop app, a scriptable CLI, and a plugin SDK, all built on one shared engine.
  image:
    src: /logo.png
    alt: Abiotic Editor
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Download
      link: https://github.com/ChristopherVR/AbioticEditor/releases/latest
    - theme: alt
      text: GitHub
      link: https://github.com/ChristopherVR/AbioticEditor

features:
  - title: Edits the whole save
    details: Reads and writes the game's GVAS saves. Anything the editor doesn't touch is left as-is, and every save keeps a .bak.
  - title: Knows the game's data
    details: Mounts the installed game's pak archives to resolve items, recipes, skills, quest flags, fish, and traits. Still works when the game isn't installed.
  - title: App and CLI, one engine
    details: The MAUI desktop editor and the headless CLI are both thin shells over a shared Core, so the CLI writes the same output as the app.
  - title: Extensible with plugins
    details: Add save operations, CLI verbs, and UI panels as .NET assemblies or plain JavaScript. Scripts need no build step.
---

## What it edits

Point the editor at a save folder and edit player saves (vitals, inventory, equipment,
skills, traits, recipes, codex, transmog, spawn point, achievements, SteamID), world saves
(containers, quest flags, doors, dropped items, NPCs, bases, story progression), and the
story metadata save. Edits stage until you save, and quest-flag changes are gated by story
prerequisites so you can't create an inconsistent narrative state.

Not sure where to begin? Head to **[Getting started](/guide/getting-started)**.
