# Bundled wiki images (offline fallback)

These PNGs are the editor's **offline fallback** for the pictures shown next to fish, vehicles,
world features, and doors. At runtime the editor always tries the live wiki first (via
`WikiImageCache`, using `Special:FilePath`), so the artwork stays current as the wiki is updated.
When the wiki is unreachable, the cache serves the matching file from this folder instead, so the
images still appear with no network.

## Source and license

Images come from **[abioticfactor.wiki.gg](https://abioticfactor.wiki.gg)** and are licensed
**CC BY-NC-SA**. Any surface that displays them must carry the credit line
`Image: abioticfactor.wiki.gg` (`WikiImageCache.AttributionText`), which the app and docs do.

## How this folder is generated

Do not edit or add files here by hand. The set is exactly the *verified* names in
`WikiImageManifest.AllFiles` (the curated entries of `FishWikiImages`, `VehicleCatalog`,
`FeatureWikiImageCatalog`, and `DoorWikiImageCatalog`). File names use
`WikiImageCache.SafeNameFor(name)` plus the real image extension, which is exactly what the cache
probes for, so the fallback lookup finds them.

Regenerate (needs network) and commit the result whenever those catalogs gain entries or the wiki
art changes:

```console
dotnet run --project src/AbioticEditor.Cli -- download-wiki-images -o assets/wiki
```

The command throttles its requests because wiki.gg rate-limits rapid bursts.

## How it is bundled

The App and CLI project files copy this folder to a `wiki\` directory next to the executable
(`CopyToOutputDirectory`), where `WikiImageCache.BundledDirectory` looks for it.
