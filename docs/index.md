---
layout: home

hero:
  name: Abiotic Editor
  text: Byte-perfect save editing for Abiotic Factor
  tagline: A desktop app, a scriptable CLI, and a plugin SDK — all over one shared engine that round-trips GVAS save files exactly.
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
  - title: Byte-for-byte safe
    details: Reads and re-serializes the game's GVAS saves losslessly. Anything the editor doesn't touch is written back identically, and every save keeps a .bak.
  - title: Knows the game's data
    details: Mounts the installed game's pak archives to resolve items, recipes, skills, quest flags, fish, and traits — and degrades gracefully when the game isn't present.
  - title: App and CLI, one engine
    details: The MAUI desktop editor and the headless CLI are thin shells over a shared Core, so scripted edits are byte-identical to what the app would write.
  - title: Extensible with plugins
    details: Add save operations, CLI verbs, and UI panels as .NET assemblies or plain JavaScript — no build step required for scripts.
---

## What it edits

Point the editor at a save folder and edit player saves (vitals, inventory, equipment,
skills, traits, recipes, codex, transmog, spawn point, achievements, SteamID), world saves
(containers, quest flags, doors, dropped items, NPCs, bases, story progression), and the
story metadata save. Edits stage until you save; quest-flag changes are gated by story
prerequisites so you can't create an inconsistent narrative state.

Not sure where to begin? Head to **[Getting started](/guide/getting-started)**.
