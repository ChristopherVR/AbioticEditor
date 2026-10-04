namespace AbioticEditor.Plugins.GameModels3D;

internal sealed partial class PakSceneModelProvider
{
    /// <summary>Maintainer-only preparation of every level and its render assets for Pages.</summary>
    internal void PrepareHostedScenery(Action<string> progress, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("An installed game and mappings are required.");
        // Without the native decoder most characters' poses cannot be read; their meshes are then never
        // cached, so they would be missing from the browser editor's scenery (377 were, in the first export).
        if (!NativeDecoder.Loaded)
            throw new InvalidOperationException("Load CUE4Parse-Natives first (NativeDecoder.TryLoad): characters' poses need it.");
        _exportFiles = new(StringComparer.Ordinal);
        _exportStandIns = [];
        foreach (var name in _mapsByName.Value.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldFor(name);
        }
        var maps = MapsToPrepare();
        var done = 0;
        foreach (var map in maps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress($"Preparing {++done}/{maps.Count}: {Path.GetFileNameWithoutExtension(map)}");
            var level = LevelFor(map);
            foreach (var group in level.Entries.Where(e => !level.HlodMeshes[e.Mesh] && e.Radius <= MaxPieceRadiusCm)
                         .GroupBy(e => (e.Mesh, e.Overrides)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = group.First();
                var mesh = level.Meshes[first.Mesh];
                var overrides = level.OverrideSets[first.Overrides];
                if (MeshInfoOf(mesh) is not { } info || IsEffectOnly(info, overrides)) continue;
                var materials = LandscapeBaker.IsKey(mesh) && overrides is [{ } terrain, ..]
                    ? TerrainMaterialOf(terrain) : MaterialsFor(info, overrides, LevelTextureSize, PoseBaker.IsKey(mesh));
                if (materials.Any(m => m.Decal && (m.Texture is null || !TextureHasAlpha(m.Texture)))) continue;
                OpenAsset($"mesh/{LevelLod}{mesh}");
                foreach (var material in materials)
                {
                    if (material.Texture is { } texture) OpenAsset(texture);
                    foreach (var layer in material.Layers ?? [])
                        if (layer.Texture is { } layerTexture) OpenAsset(layerTexture);
                }
            }
        }
        WriteJson(Path.Combine(_cacheRoot.Value, "hosted-files.json"), _exportFiles.Keys.Order(StringComparer.Ordinal).ToArray());
        progress($"Prepared {_exportFiles.Count} scenery cache entries for Pages.");
        foreach (var standIn in _exportStandIns.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            progress($"Not prepared (no exact pose, left out of Pages): {standIn}");
        _exportFiles = null;
        _exportStandIns = null;
    }
}
