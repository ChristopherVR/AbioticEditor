# Desktop scenery on GitHub Pages

This directory is a Pages-only data source. It is never linked into a desktop, CLI or
browser editor project. Its main reader is the browser editor, which cannot read an installed
game: `HostedSceneryReader` (Web.Shared) answers the 3D view's level queries from
`https://christophervr.github.io/AbioticEditor/scenery/v1/`, beside `/app/`, exactly as the
desktop provider answers them from a full cache. It draws the build `v1/index.json` names as
`latest`. The desktop GameModels3D provider also downloads files from here, but only when its
installed game's signature matches a hosted build exactly; after a game update it reads the
game itself. The desktop publish target rejects any attempt to include this directory in the
app package.

Each build directory is named for a portable signature of the installed base game's
archive indexes, archive sizes and mappings. Pak footers and complete IoStore tables
are hashed. Install paths and dates do not affect this signature. Asset mods bypass
hosted data. Unknown builds and connection failures use local game extraction.

The format is the existing render cache: compact binary level indexes, ABM1 meshes,
PNG textures, JSON world layouts, materials and class descriptions. Streaming volumes,
terrain weights, actor identities, lights and the alternate open door leaves stay intact.
Each manifest entry records its byte count and SHA-256 digest. Downloads are size bounded,
verified before an atomic cache write, and reused on future visits. Source ZIP chunks
stay below 80 MiB uncompressed so they fit in ordinary GitHub repository files.

## Refreshing scenery

Use an installed, unmodified game matching the mappings shipped with the app. Close other
maintainer probes before rebuilding them. This reads game files and writes derived cache
files only; it does not change game files or saves.

```powershell
$env:ABIOTIC_SCENERY_PREPARE = '1'
dotnet test tests/AbioticEditor.Probes -c Release --filter FullyQualifiedName~HostedSceneryExportProbe
Remove-Item Env:ABIOTIC_SCENERY_PREPARE
python tools/scenery.py export --cache '<plugin-data>/cache/<current install stamp>' --paks '<game>/AbioticFactor/Content/Paks' --mappings assets/Mappings.usmap
python tools/scenery.py assemble --destination artifacts/scenery-preview/scenery
```

The export command checks that the cache stamp matches the current installed game.
The preparation probe records the files needed by level scenery in `hosted-files.json`;
the export uses this inventory to leave unrelated cached object previews out of Pages.
It refuses to overwrite an existing signature directory. Prepare and verify a replacement
outside this source directory when refreshing the same game build, then replace its source
chunks deliberately. Commit the complete export with its manifests. Do not place raw paks,
personal saves or user settings here.

Level indexes are cached (and packed here) as `levels/<hash>.bin` but published as
`levels/<hash>.ali`. Browser download managers such as IDM capture requests by the address's
file extension, including a page's own background requests, and `.bin` is on their default
lists: the browser editor's level requests came back empty and could prompt the player to save
each file. `assemble` refuses to publish any name ending in a commonly captured extension, and
both downloaders ask for the published name (`HostedSceneryCache.PublishedPath`,
`HostedSceneryReader.PublishedPath`). The manifest keeps the cache names.

`docs.yml` verifies and extracts every source chunk into the combined Pages artifact,
outside `/app/`, and writes `v1/index.json`. With more than one build here, put the newest
build's signature in `latest.txt` so the browser editor draws it. It fails on missing files, duplicate names, unsafe paths, incorrect
sizes/hashes or a combined site larger than 900 MiB. Old build exports must be removed
when necessary to keep the site within this budget; desktop clients retain downloaded
data and can fall back to their own game files.
