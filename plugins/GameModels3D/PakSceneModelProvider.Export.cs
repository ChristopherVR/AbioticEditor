using AbioticEditor.Plugins.Scene;

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

    /// <summary>
    /// Maintainer-only: prepares how every placeable class looks (and the meshes and textures it draws)
    /// for the browser editor, which has no game to read. Classes are found by their blueprint naming
    /// (deployed objects and vehicles), never from a list of names, so a game update's new objects are
    /// included. The inventory it writes is added to the one a level preparation left.
    /// </summary>
    internal void PrepareHostedClasses(Action<string> progress, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("An installed game and mappings are required.");
        _exportFiles = new(StringComparer.Ordinal);
        var classes = PlaceableClassPaths();
        var done = 0;
        var modelled = 0;
        foreach (var classPath in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++done % 25 == 0) progress($"Preparing object models {done}/{classes.Count}");
            try
            {
                var plain = DescribeClass(classPath);
                if (plain is null) continue;
                modelled++;
                var models = new List<SceneClassModel> { plain };
                // A colour only gets its own entry where the class has paintable materials: the browser
                // draws every other colour of the class as its plain model.
                foreach (var colour in AbioticEditor.Core.WorldSaves.DeployablePaintCatalog.Colors)
                {
                    if (Read(p => PaintResolver.Materials(p, classPath, colour.Value)) is null) continue;
                    if (DescribeClass(classPath, colour.Value) is { } painted) models.Add(painted);
                }
                foreach (var part in models.SelectMany(m => m.Parts))
                {
                    OpenAsset(part.Mesh);
                    foreach (var material in part.Materials)
                    {
                        if (material.Texture is { } texture) OpenAsset(texture);
                        foreach (var layer in material.Layers ?? [])
                            if (layer.Texture is { } layerTexture) OpenAsset(layerTexture);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                progress($"No model for {classPath}: {ex.Message}");
            }
        }
        // Only what the browser asks for: the answers and the files they name.
        var keys = _exportFiles.Keys
            .Where(k => k.StartsWith(ClassesFolder + "/", StringComparison.Ordinal) || k.StartsWith("meshes", StringComparison.Ordinal) || k.StartsWith("textures/", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var inventory = Path.Combine(_cacheRoot.Value, "hosted-files.json");
        if (TryReadJson<string[]>(inventory) is { } before) keys.UnionWith(before);
        WriteJson(inventory, keys.Order(StringComparer.Ordinal).ToArray());
        progress($"Prepared {modelled} of {classes.Count} object models; {keys.Count} scenery cache entries in all for Pages.");
        _exportFiles = null;
    }

    /// <summary>
    /// Class paths of everything the editor draws as a placed object: blueprints under the game's
    /// Blueprints folder named <c>Deployed_*</c> or <c>ABF_Vehicle*</c>, as a save writes them
    /// (<c>/Game/Blueprints/.../Deployed_X.Deployed_X_C</c>).
    /// </summary>
    private static List<string> PlaceableClassPaths()
    {
        const string marker = "/Content/";
        var result = new List<string>();
        foreach (var asset in Assets()?.AssetPaths ?? [])
        {
            if (!asset.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
            var at = asset.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var package = "/Game/" + asset[(at + marker.Length)..^".uasset".Length];
            if (!package.StartsWith("/Game/Blueprints/", StringComparison.OrdinalIgnoreCase)) continue;
            var name = package[(package.LastIndexOf('/') + 1)..];
            if (name.StartsWith("Deployed_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ABF_Vehicle", StringComparison.OrdinalIgnoreCase))
                result.Add($"{package}.{name}_C");
        }
        return result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }
}
