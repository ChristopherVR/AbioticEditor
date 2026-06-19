# Bundled game-data registry

`registry.json` here is a pre-extracted snapshot of Abiotic Factor's data tables. The editor
loads it so its catalogs (items, and more over time) work even with no game installed. It is the
generated, broader successor to the hand-written `Fallback` tables in Core.

It does NOT carry icons/textures or fonts (those are binary pak assets and still need the live
install). The registry stores icon *paths*, so the editor shows names and stats offline and fills
in icons only when the game is present.

## Regenerating (maintainer step, per game patch)

Run the CLI against a real install, then copy the result here and commit it:

```console
dotnet run --project src/AbioticEditor.Cli -- dump-registry --output assets/registry/registry.json --game-version <build>
```

`--game-dir <folder>` points at a non-Steam install; otherwise it auto-detects via Steam or
`ABIOTIC_GAME_DIR`. The build needs `Mappings.usmap` next to the CLI (it is bundled).

## How it's loaded

`GameDataRegistry.LoadBundled()` resolves, in order:

1. `%LOCALAPPDATA%/AbioticEditor/registry/registry.json` (user override - drop in a fresh dump
   without updating the editor), then
2. `registry/registry.json` next to the executable (this bundled file).

Live pak data always wins when the game is installed; the registry is the fallback.
