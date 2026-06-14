# Wiki sources

These Markdown files are the source for the project's [GitHub
Wiki](https://github.com/ChristopherVR/AbioticEditor/wiki). GitHub serves a wiki from a separate
git repository (`AbioticEditor.wiki.git`), so the files here are staged copies you publish to that
repo.

## Page naming

GitHub maps a wiki page title to a file name by replacing spaces with hyphens. The names here follow
that convention:

| File | Wiki page |
|---|---|
| `Home.md` | Home (the wiki landing page) |
| `Adding-Plugins.md` | Adding Plugins |
| `Building-Plugins.md` | Building Plugins |
| `Plugin-API-Reference.md` | Plugin API Reference |
| `_Sidebar.md` | the wiki sidebar (shown on every page) |

## Publishing

```bash
# one-time: clone the wiki repo next to this one
git clone https://github.com/ChristopherVR/AbioticEditor.wiki.git

# each update: copy these files over and push
cp wiki/*.md ../AbioticEditor.wiki/
cd ../AbioticEditor.wiki
git add -A && git commit -m "Update plugin docs" && git push
```

The fuller, versioned documentation lives on the docs site
(<https://christophervr.github.io/AbioticEditor/>) and under [`docs/`](../docs). The wiki is a
lighter, task-focused entry point for plugin authors and users.
