# Maintainer commands

These CLI commands regenerate data that ships **with** the editor. They are for maintainers preparing
a release, not for everyday editing: you run them when a new game build or a wiki change means the
bundled data needs refreshing, and you commit their output to the repo. Everyday CLI usage is covered
in the [command-line tool guide](/guide/cli).

```console
abioticeditor dump-registry -o registry.json        # dump the game's data tables (needs the game installed)
abioticeditor download-wiki-images -o assets/wiki    # fetch the offline wiki-image fallback (needs network)
```

## `dump-registry`

Dumps the game's item / recipe / skill / flag / fish / trait data tables to JSON. Needs the game
installed (it reads the pak archives through the bundled type-mappings). This is the registry the
editor falls back on, so regenerate and commit it when a game update changes the catalogs.

## `download-wiki-images`

Downloads the verified fish / vehicle / world-feature / door reference pictures from
[abioticfactor.wiki.gg](https://abioticfactor.wiki.gg) into a folder the app and CLI bundle as the
**offline fallback**. The live wiki is still tried first at runtime, so the bundled art only shows
when the wiki is unreachable, and stays current otherwise. The command throttles its requests
because the wiki rate-limits rapid bursts. The images are CC BY-NC-SA 4.0; see `assets/wiki/README.md`.

The bundled set lives in `assets/wiki/`. See where these pictures surface in the app under
[Reference pictures from the wiki](/guide/desktop-app#reference-pictures-from-the-wiki).

## Regenerating the bundled usmap

The bundled `Mappings.usmap` is validated for a known-good game build. Refreshing it for a new build
uses the same dump-and-replace flow an end user follows to
[keep game data current](/guide/game-data#the-usmap-matching-the-game-build), except the maintainer
replaces the file bundled in `assets/` and commits it, rather than dropping it in the per-user
mappings folder.
