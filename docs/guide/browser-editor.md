# Edit in your browser

There is nothing to install. **[Open the editor](/app/)**, point it at your save folder, and
edit exactly as you would in the desktop app.

Your saves never leave your computer. There is no server: the editor is a program that runs
inside the page, and it reads the folder you choose directly off your disk. Nothing is
uploaded anywhere.

## Which folder to choose

This is the one thing worth getting right, because picking the wrong level costs you features.

Choose the folder for the **account**, not one world:

```
SaveGames/
└── 76561198000000000/     <- choose THIS one
    ├── ScientistCustomization_1.sav
    └── Worlds/
        ├── Cascade/
        └── Chrissie/
```

You can open a single world folder (`Cascade` above) and everything inside it works. But your
character's **look** is stored next to `Worlds/`, not inside it, so a browser given only one
world cannot see it. Open the account folder and every world is listed together, with the
appearance editor working too.

::: tip Finding it on your computer
On Windows, saves live under
`%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<your SteamID>`.
Paste that into the address bar of the folder chooser.
:::

Press **OPEN FOLDER**, choose that folder, and allow the browser to view it when it asks.

You can also **drag the folder onto the page** instead of using the chooser - anywhere on the
window will do.

## Coming back later

The editor remembers the last three worlds you opened and offers them on its home page, so a
refresh does not mean hunting through the folder chooser again. Press **OPEN** on one and your
browser will ask permission to read that folder once more (it deliberately forgets that
permission when you close the tab). If a world has been moved or deleted since, the editor says
so and you can remove it from the list.

Nothing about your save is stored: what is remembered is a bookmark to the folder, not its
contents.

## Opening a zip

**OPEN A ZIP** on the home page takes a zipped save folder - either the one **EXPORT** gave
you, or one you made yourself by zipping the world folder. You can drop a `.zip` onto the page
too.

This is the way back in for Firefox and Safari, where the editor cannot reopen a folder: export
your zip before you close the tab, then drop it back on next time and carry on. Saves opened
from a zip are edited in the tab and leave the same way, through **EXPORT**.

## Your browser matters

| Browser | What happens |
| --- | --- |
| **Chrome, Edge, Opera** | Full use. The editor writes changes straight back to your save folder, keeping a `.bak` of the old file each time, exactly like the desktop app. |
| **Firefox, Safari** | Read-only. The editor can open your folder and edit everything, but the browser will not let any web page write to your disk. Your changes live in the tab until you **export** them. |

On Firefox and Safari, nothing is lost as long as you export before closing the tab. The
editor says so on screen rather than letting you find out afterwards, and the browser itself
will ask you to confirm before you close or reload a tab holding unsaved edits.

## Getting your saves back out

**EXPORT**, above the save list, downloads every file in the world as one zip, laid out the
same way the game's folder is, so you can copy it straight back over your save folder.

Take the whole set rather than one file wherever you can: editing one save can quietly change
others. Setting a story chapter, for example, rewrites the world save and can rewrite every
player save with it.

If you are certain you only want one file, **right-click any save in the list** and choose
**EXPORT THIS SAVE**. That gives you just that `.sav`.

::: warning Export saves what is on disk
Press **SAVE** first. An edit still open in the editor is not in the export.
:::

## What the browser cannot do

A web page is not allowed to roam your computer, so a few things are hidden in the browser and
show a "needs the desktop editor" note instead. Everything else is the same editor.

| Not in the browser | Why |
| --- | --- |
| Compare two saves | Picks two saves from anywhere on your machine. |
| Create a new world | Writes a whole folder of new files. |
| Settings files (`.ini`) | Found by searching folders on your computer. |
| Achievements | Needs the Steam files installed on your computer. |
| Finding your saves automatically | The editor cannot look around your disk; you point it at the folder. |
| Plugins | Load code, which a page cannot do. |

Everything else - players, inventory, skills, recipes, the codex, worlds, containers, quest
flags, pets, vehicles, story progression, your character's look, raw JSON import and export -
works the same as the [desktop app](/guide/desktop-app).

## If you would rather install it

The [desktop app](/guide/desktop-app) has no such limits, reads your game's own item icons and
names from your installed copy, and finds your saves for you.
[Download it here](https://github.com/ChristopherVR/AbioticEditor/releases/latest).
