# Wiki source files

These files are the staged source for the project's [GitHub Wiki](https://github.com/ChristopherVR/AbioticEditor/wiki). The wiki is a quick player-facing field guide with a small plugin corner. The maintained full handbook is the [documentation site](https://christophervr.github.io/AbioticEditor/guide/).

GitHub stores a wiki in a separate repository, `AbioticEditor.wiki.git`. Copy these files there when publishing a wiki update.

## Pages

| File | Wiki page | Audience |
| --- | --- | --- |
| `Home.md` | Home | Players, with links into the main handbook |
| `Adding-Plugins.md` | Adding Plugins | Players installing a community plugin |
| `Building-Plugins.md` | Building Plugins | Plugin authors |
| `Plugin-API-Reference.md` | Plugin API Reference | Plugin authors |
| `_Sidebar.md` | Sidebar | Navigation shown on every page |

## Publish an update

```console
# one time: clone the wiki alongside this repository
git clone https://github.com/ChristopherVR/AbioticEditor.wiki.git

# copy the staged pages, review them, then publish from the wiki repository
cp wiki/*.md ../AbioticEditor.wiki/
cd ../AbioticEditor.wiki
git add -A
git commit -m "Update field guide"
git push
```

Keep the wording welcoming and practical. Link player tasks to the documentation site; keep build instructions, APIs, and implementation details in the technical reference or this repository's `docs/` folder.
