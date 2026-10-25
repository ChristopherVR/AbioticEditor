# Abiotic Editor field manual

Abiotic Editor is a fan-made save editor for **Abiotic Factor**. Use it to restock a cupboard, rescue a stuck story character, rearrange your base, adjust a skill, or move supplies between worlds. You do not need to know anything about file formats.

New here? Start with [Getting started](./getting-started).

## What can it do?

**Your character.** Health and needs, inventory and equipment, skills, recipes, traits, appearance, transmog, respawn point, GatePal entries and carried pets.

In **Recipes**, choose **Show crafting steps** to trace an item back to its materials. Choose **How to unlock** for a connected tree of the selected recipe's learning requirements. Its discovery branches are alternatives: pick one supported method, rather than completing them all. A pickup branch requires every listed item type, while a substitute branch needs any one member. Expand another recipe to follow its own discovery paths. Skill levels, conversations, objects, workbenches, cooking, chemistry, compendium, emails and fish appear in the same tree. **Inspect item** opens the existing material sources or recipe details as optional help.

Learning pickups mean having collected the required types before, not owning the crafting quantities now. Research follows discovery as a separate step; **Discovered, research pending** takes precedence over the Unlocked checkbox. Expand or collapse branches as needed. Switching to **Show crafting steps** keeps learning requirements separate from crafting costs.

**Check for missed recipes** uses supported learning conditions and your recorded discoveries to suggest unlearned recipes. Each suggestion explains its clue. These are clues to investigate, not guaranteed missed discoveries: some story access and alternative methods are not checked. Choose **Unlock recipe** to stage one, then **SAVE**; **Undo unlock** reverses it before saving. The usual spoiler and story checks still apply. Learning sources have their own reveal control. Browsing never changes recipes, quests or research. The recipe data disclosure shows installed game files or a snapshot. Opening a recipe task or unlocking checks for source changes, reloading learning metadata with recipes, item names and discovery tables. Snapshots need an editor update or newer import after game updates. Older imports without learning metadata keep unknown methods honest.

For a **Find** material in the crafting guide, choose **Sources**. The details pane lists known salvage inputs, trader offers or fishing locations, with extra sources expandable and future content concealed. Salvage may yield the listed amounts; trader offers do not establish a paired price. Getting a material is separate from learning a recipe. Missing catalog entries, unresolved ingredient groups and materials without listed sources have different explanations.

**Your world.** Containers, doors, items on the ground, story characters and creatures, pets, vehicles, story progress, traders, containment cells, garden plots, chemistry benches, resource nodes, buttons, elevators, trams and more. The world lists show a picture of each thing and where it stands.

**Your base, in 3D.** See your base drawn with the game's own models, walk around it, move and copy pieces, place new ones and wire up power. **Show in 3D** takes you to anything you pick in a list. See [Bases and the 3D view](./3d-view).

**Repairs and moves.** Compare two saves, move items between worlds, convert between Steam and Game Pass, repair a Game Pass save, and adjust dedicated-server settings.

**Live editing.** Change a world while the game is running, through a small helper the editor installs. See [Live editing](./live-editing).

Every change to a save file waits until you press **SAVE**, and the editor keeps the old file as a `.bak` copy.

## Where it runs

| Version | Runs on | Good for |
| --- | --- | --- |
| [Desktop app](./desktop-app) | Windows, Linux, Steam Deck and macOS | Everything: finding saves, the 3D view with game models, transfers, Game Pass, live editing |
| [Browser editor](./browser-editor) | Any modern browser | A quick edit with nothing to install |
| [Command-line tool](./cli) | Windows, Linux and macOS | Scripts and server admins |

## Pick your route

| I want to... | Start here |
| --- | --- |
| Make a safe first change | [Getting started](./getting-started) |
| Find my way around the app | [Desktop app tour](./desktop-app) |
| See and change my base in 3D | [Bases and the 3D view](./3d-view) |
| Edit gardens, benches, pets and other world tools | [More world tools](./review-features) |
| Edit without installing anything | [Edit in your browser](./browser-editor) |
| Move gear between two worlds | [Transfer items](./transfer-items) |
| Use a Game Pass save | [Game Pass saves](./game-pass) |
| Set up on Linux or Steam Deck | [Linux and Steam Deck](./linux-local-host) |
| Fix missing item names or pictures | [Game data](./game-data) |
| Change things while the game is running | [Live editing](./live-editing) |
| Add community tools or a language | [Plugins and language packs](./plugins) |
| See every screen first | [Screenshot tour](./screenshots) |

::: tip Before any expedition
Close the game or stop the server before editing save files. The editor keeps a `.bak` copy when it writes a file, but a separate copy of the whole world folder is the safest recovery kit.
:::

The [technical reference](../reference/) explains how the editor works, how to make plugins and how to contribute. You can skip it if your goal is getting back to science.
