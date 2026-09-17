# Bundled game-data registry

`registry.json` here is a pre-extracted snapshot of Abiotic Factor's data tables. The editor
loads it so its catalogs (items, and more over time) work even with no game installed. It is the
generated, broader successor to the hand-written `Fallback` tables in Core.

It does NOT carry icons/textures or fonts (those are binary pak assets and still need the live
install). The registry stores icon *paths*, so the editor shows names and stats offline and fills
in icons only when the game is present.

## Regenerating (maintainer step, per game patch)

This folder holds one file per language the game ships (`registry.<culture>.json`) plus a
culture-less `registry.json`, the fallback every host loads when the player's own language did
not ship. Regenerate all of them together, then copy the result here and commit it:

```console
dotnet run --project src/AbioticEditor.Cli -- dump-registry --all-cultures --output assets/registry --game-version <build>
```

`--game-dir <folder>` points at a non-Steam install; otherwise it auto-detects via Steam or
`ABIOTIC_GAME_DIR`. The build needs `Mappings.usmap` next to the CLI (it is bundled). A single
culture can still be dumped on its own with `dump-registry --culture <code> --output <file>`, but
`--all-cultures` is what keeps this folder's file set matching every language the editor's
language picker (`HostLanguageService.SupportedGameDataCodes`) offers.

## How it's loaded

`GameDataRegistry.LoadBundled()` resolves, in order:

1. `%LOCALAPPDATA%/AbioticEditor/registry/registry.json` (user override - drop in a fresh dump
   without updating the editor), then
2. `registry/registry.json` next to the executable (this bundled file).

Live pak data always wins when the game is installed; the registry is the fallback.
