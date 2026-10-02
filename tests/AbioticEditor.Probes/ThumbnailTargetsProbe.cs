using System.IO;
using System.Text.Json;
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
        ("TramMap", "trams"), ("PortalMap", "portals"), ("ResourceNodeMap", "resource-nodes"),
    ];

    [GeneratedRegex(@"^(?<cls>.+_C)_\d+$")]
    private static partial Regex InstanceSuffix();

    /// <summary>The kind of an actor, from its instance name: <c>BlastDoor_C_11</c> is a <c>BlastDoor_C</c>.</summary>
    public static string ClassOf(string actorPath)
    {
        var name = actorPath[(Math.Max(actorPath.LastIndexOf('.'), actorPath.LastIndexOf(':')) + 1)..];
        var match = InstanceSuffix().Match(name);
        return match.Success ? match.Groups["cls"].Value : name;
    }

    [Fact]
    public void List_one_actor_per_kind()
    {
        var dir = Environment.GetEnvironmentVariable("THUMBNAIL_SAVES_DIR");
        var output = Environment.GetEnvironmentVariable("THUMBNAIL_TARGETS_OUT");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(output)) return;

        var targets = new Dictionary<(string Kind, string Cls), object>();
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
                // A plain mesh placed in the level has no kind of its own to picture.
                if (key.Item2.StartsWith("StaticMeshActor", StringComparison.Ordinal)) return;
                targets.TryAdd(key, new { kind, cls = key.Item2, region, actor = actorPath });
            }
            foreach (var door in data.Doors) Add("doors", door.Id);
            foreach (var (map, kind) in Lists)
                foreach (var entry in WorldMapAccessor.Entries(data.Raw, map)) Add(kind, entry.Key);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(targets.Values, new JsonSerializerOptions { WriteIndented = true }));
    }
}
