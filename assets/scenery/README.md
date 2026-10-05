# Browser editor scenery on GitHub Pages

This directory is a Pages-only data source for the **browser editor**, which cannot read an
installed game. It is never linked into a desktop, CLI or browser editor project, and the
**desktop app never downloads it**: the desktop GameModels3D provider reads the player's own
installed game. The desktop publish target rejects any attempt to include this directory in the
app package.

`HostedSceneryReader` (Web.Shared, registered only by the browser host) answers the 3D view's
level queries from `https://christophervr.github.io/AbioticEditor/scenery/v1/`, beside `/app/`,
exactly as the desktop provider answers them from a full cache of the same files. It draws the
build `v1/index.json` names as `latest`.

Each build directory is named for a portable signature of the base game's archive indexes,
archive sizes and mappings (`signature` in `tools/scenery.py`). Pak footers and complete IoStore
tables are hashed; install paths and dates do not affect it.

The format is the desktop provider's render cache: compact binary level indexes, ABM1 meshes,
PNG textures, JSON world layouts, materials and class descriptions. Streaming volumes, terrain
weights, actor identities, lights and the alternate open door leaves stay intact. Each manifest
entry records its byte count and SHA-256 digest; the manifest is checked when the site is
assembled and is not published. Source ZIP chunks stay below 80 MiB uncompressed so they fit in
ordinary GitHub repository files.

## Refreshing scenery

Use an installed, unmodified game matching the mappings shipped with the app. Close other
maintainer probes before rebuilding them. This reads game files and writes derived cache files
only; it does not change game files or saves.

Characters' poses need CUE4Parse's native decoder. Build it as the release does
(`cmake -S submodules/CUE4Parse/CUE4Parse-Natives -B natives-build`, then
`cmake --build natives-build --config Release`) and point `ABIOTIC_NATIVES_DIR` at the folder
holding `CUE4Parse-Natives.dll`; preparation refuses to run without it, and lists any pose it
still could not bake. (The first export ran without it and left 377 characters out.)

```powershell
$env:ABIOTIC_SCENERY_PREPARE = '1'
$env:ABIOTIC_NATIVES_DIR = '<natives-build>\Release'
dotnet test tests/AbioticEditor.Probes --filter FullyQualifiedName~HostedSceneryExportProbe.Prepare_all_levels_for_Pages
Remove-Item Env:ABIOTIC_SCENERY_PREPARE
python tools/scenery.py export --cache '<plugin-data>/cache/<current install stamp>' --paks '<game>/AbioticFactor/Content/Paks' --mappings assets/Mappings.usmap
python tools/scenery.py assemble --destination artifacts/scenery-preview/scenery
```

Placed objects (furniture, benches, containers, vehicles) are prepared by a second probe, which
needs no native decoder. It finds every blueprint under `DeployedObjects`, and every `Deployed_*` and `ABF_Vehicle*` one, by name (never
a list of classes, so a game update's new objects are included), describes each plain and in
each paint colour it can wear, and adds the answers and the meshes and textures they name to
`hosted-files.json`. Add them to an existing export with `--extend`:

```powershell
$env:ABIOTIC_SCENERY_PREPARE = '1'
dotnet test tests/AbioticEditor.Probes --filter FullyQualifiedName~HostedSceneryExportProbe.Prepare_object_models_for_Pages
Remove-Item Env:ABIOTIC_SCENERY_PREPARE
python tools/scenery.py export --extend --cache '<plugin-data>/cache/<current install stamp>' --paks '<game>/AbioticFactor/Content/Paks' --mappings assets/Mappings.usmap
```

`assemble` publishes the answers together as `classes.json` per build (about 1 MB, 140 KB
compressed), which the browser loads once; the single files stay as a fallback. A garden's crops
(one entry per spot, crop and stage) and a tank's liquid level (surface empty and full, plus each
liquid's material, in `liquids-v1`) are prepared too; the browser combines them for each saved object.

The export command checks that the cache stamp matches the current installed game. The
preparation probe records the files needed by level scenery in `hosted-files.json`; the export
uses this inventory to leave unrelated cached object previews out of Pages. It refuses to
overwrite an existing signature directory; `--extend` instead adds, as new chunks, only the files
an existing export of the same build lacks. Commit the complete export with its manifests. Do not
place raw paks, personal saves or user settings here.

To check an assembled build draws with no game at all, set `ABIOTIC_SCENERY_VERIFY_ROOT` to its
`scenery/v1/<signature>` folder and run `HostedSceneryExportProbe.Pages_export_renders_without_local_extraction`.

## Names download managers leave alone

Level indexes are cached (and packed here) as `levels/<hash>.bin` but published as
`levels/<hash>.ali`. Browser download managers such as IDM capture requests by the address's
file extension, including a page's own background requests, and `.bin` is on their default
lists: the browser editor's level requests came back empty and could prompt the player to save
each file. `assemble` refuses to publish any name ending in a commonly captured extension, and
the browser reader asks for the published name (`HostedSceneryReader.PublishedPath`).

## Publishing

`docs.yml` verifies and extracts every source chunk into the combined Pages artifact, outside
`/app/`, and writes `v1/index.json`. With more than one build here, put the newest build's
signature in `latest.txt` so the browser editor draws it. It fails on missing files, duplicate
names, unsafe paths, incorrect sizes/hashes or a combined site larger than 980 MiB (GitHub Pages refuses 1 GiB). Old build
exports must be removed when necessary to keep the site within this budget.
