using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

/// <summary>
/// Lists what tools/thumbnails renders: one level actor per kind of door, button, breakable wall,
/// elevator, tram, teleporter and resource node found in a world's region saves, with the region to
/// draw it in. Set <c>THUMBNAIL_SAVES_DIR</c> to a world folder and <c>THUMBNAIL_TARGETS_OUT</c> to the
/// JSON file to write. Not part of the normal test run.
/// </summary>
public sealed partial class ThumbnailTargetsProbe
{
    /// <summary>The world lists whose entries are level actors, and the picture folder each uses.</summary>
    private static readonly (string Map, string Kind)[] Lists =
    [
        ("ButtonMap", "buttons"), ("DestructibleMap", "destructibles"), ("ElevatorMap", "elevators"),
        ("TramMap", "trams"), ("PortalMap", "portals"), ("ResourceNodeMap", "resource-nodes"), ("PowerSocketMap", "power-sockets"),
    ];

    [GeneratedRegex(@"^(?<cls>.+_C)_\d+$")]
    private static partial Regex InstanceSuffix();

    /// <summary>Mirrors WorldThumbnails.PowerOutletOwner in the editor (this probe does not reference the web project).</summary>
    private static class WorldThumbnailKeys
    {
        public static string? PowerOutletOwner(string key)
            => key.Length == 33 && key[..32].All(Uri.IsHexDigit) && char.IsDigit(key[32]) ? key[..32] : null;
    }

    /// <summary>The kind of an actor, from its instance name: <c>BlastDoor_C_11</c> is a <c>BlastDoor_C</c>.</summary>
    public static string ClassOf(string actorPath)
    {
        var name = actorPath[(Math.Max(actorPath.LastIndexOf('.'), actorPath.LastIndexOf(':')) + 1)..];
        var match = InstanceSuffix().Match(name);
        return match.Success ? match.Groups["cls"].Value : name;
    }

    /// <summary>
    /// Adds each kind's blueprint path (read from the installed game, when there is one) so render.mjs
    /// can draw a kind from its own model when its level actor cannot be found or drawn: resource
    /// nodes spawned into the level at run time (wood crates in the reactors, essences) have no fixed
    /// place in the map to draw them at.
    /// </summary>
    private static IEnumerable<object> WithClassPaths(IEnumerable<object> targets)
    {
        Dictionary<string, string>? packages = null;
        try
        {
            using var provider = AbioticEditor.Core.Assets.GameAssetProvider.CreateForLocalInstall();
            if (provider is not null)
            {
                packages = new(StringComparer.OrdinalIgnoreCase);
                const string content = "AbioticFactor/Content/";
                foreach (var file in provider.AssetPaths)
                {
                    if (!file.StartsWith(content, StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
                    var package = "/Game/" + file[content.Length..^".uasset".Length];
                    packages.TryAdd(package[(package.LastIndexOf('/') + 1)..], package);
                }
            }
        }
        catch (Exception) { packages = null; }
        foreach (var target in targets)
        {
            var json = JsonSerializer.SerializeToNode(target)!.AsObject();
            var cls = json["cls"]!.GetValue<string>();
            if (packages is not null && cls.EndsWith("_C", StringComparison.Ordinal) && packages.TryGetValue(cls[..^2], out var package))
            {
                json["classPath"] = $"{package}.{cls}";
                // A tram kind's picture is its own model, unpainted: the trams in the level each look different.
                if (json["kind"]!.GetValue<string>() == "trams") json.Remove("actor");
            }
            yield return json;
        }
    }

    [Fact]
    public void List_one_actor_per_kind()
    {
        var dir = Environment.GetEnvironmentVariable("THUMBNAIL_SAVES_DIR");
        var output = Environment.GetEnvironmentVariable("THUMBNAIL_TARGETS_OUT");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(output)) return;

        var targets = new Dictionary<(string Kind, string Cls), object>();
        var instances = new List<object>();
        var classes = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(dir, "WorldSave_*.sav").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var region = Path.GetFileNameWithoutExtension(path)["WorldSave_".Length..];
            if (region.Equals("MetaData", StringComparison.OrdinalIgnoreCase)) continue;
            WorldSaveData data;
            try { data = WorldSaveReader.ReadFromFile(path); }
            catch (Exception) { continue; }

            void Add(string kind, string actorPath)
            {
                if (!actorPath.StartsWith("/Game/", StringComparison.Ordinal)) return;
                var key = (kind, ClassOf(actorPath));
                // A plain mesh placed in the level has no kind of its own to picture (but has a place).
                if (key.Item2.StartsWith("StaticMeshActor", StringComparison.Ordinal)) { instances.Add(new { kind, cls = key.Item2, region, actor = actorPath }); return; }
                targets.TryAdd(key, new { kind, cls = key.Item2, region, actor = actorPath });
                // Each tram is painted its own colour (and one is a round car), so each gets its own picture.
                if (kind == "trams")
                {
                    var name = actorPath[(Math.Max(actorPath.LastIndexOf('.'), actorPath.LastIndexOf(':')) + 1)..];
                    targets.TryAdd(("trams-each", name), new { kind = "trams-each", cls = name, region, actor = actorPath });
                }
                instances.Add(new { kind, cls = key.Item2, region, actor = actorPath });
            }
            foreach (var door in data.Doors) Add("doors", door.Id);
            // Containers are drawn from their own model (classThumbnail), so they need the class path.
            var classPaths = (PlacedObjectCensus.Build(data).Objects ?? [])
                .Where(o => o.ClassPath is not null)
                .GroupBy(o => o.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().ClassPath!, StringComparer.Ordinal);
            foreach (var container in data.Containers)
                if (container.ClassName is { Length: > 0 } cls && classPaths.TryGetValue(container.Id, out var classPath))
                    classes.TryAdd(cls, new { kind = "containers", cls, classPath });
            foreach (var (map, kind) in Lists)
                foreach (var entry in WorldMapAccessor.Entries(data.Raw, map)) Add(kind, entry.Key);
            // Lists of player-placed things (teleporter pads, sconce lamps, garden plots...) show the
            // placed object's own class picture, and so does a power outlet on a player-built device
            // (its key is the device's id plus one outlet digit).
            void AddPlaced(string? id)
            {
                if (id is null || !classPaths.TryGetValue(id, out var classPath)) return;
                if (PlacedObjectCensus.ClassNameOf(classPath) is { Length: > 0 } cls)
                    classes.TryAdd(cls, new { kind = "deployables", cls, classPath });
            }
            foreach (var feature in WorldMapFeatures.All.Where(f => f.MapName == "DeployedObjectMap" && f.AppliesTo(data.Raw)))
                foreach (var entry in feature.Read(data.Raw)) AddPlaced(entry.Key);
            foreach (var entry in WorldMapAccessor.Entries(data.Raw, "PowerSocketMap"))
                if (WorldThumbnailKeys.PowerOutletOwner(entry.Key) is { } owner) AddPlaced(owner);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(WithClassPaths(targets.Values), new JsonSerializerOptions { WriteIndented = true }));
        if (Environment.GetEnvironmentVariable("THUMBNAIL_CLASSES_OUT") is { Length: > 0 } classesOut)
            File.WriteAllText(classesOut, JsonSerializer.Serialize(classes.Values, new JsonSerializerOptions { WriteIndented = true }));
        if (Environment.GetEnvironmentVariable("THUMBNAIL_INSTANCES_OUT") is { Length: > 0 } instancesOut)
            File.WriteAllText(instancesOut, JsonSerializer.Serialize(instances, new JsonSerializerOptions { WriteIndented = true }));
    }
}
