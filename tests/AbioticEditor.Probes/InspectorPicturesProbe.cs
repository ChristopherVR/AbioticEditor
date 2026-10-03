using System.IO;
using System.Text.Json;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

/// <summary>Finds the installed game's corpse and lamp blueprints for tools/thumbnails.</summary>
public sealed class InspectorPicturesProbe
{
    [Fact]
    public void Write_all_fixed_location_targets()
    {
        var output = Environment.GetEnvironmentVariable("LOCATION_PICTURES_OUT");
        if (string.IsNullOrEmpty(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        Assert.NotNull(assets);
        var provider = (CUE4Parse.FileProvider.DefaultFileProvider)typeof(GameAssetProvider)
            .GetField("_provider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(assets)!;
        var targets = new List<object>();
        foreach (var path in assets!.AssetPaths.Where(p => p.StartsWith("AbioticFactor/Content/Maps/", StringComparison.Ordinal)
            && p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)))
        {
            var map = Path.GetFileNameWithoutExtension(path);
            foreach (var lazy in provider.LoadPackage(path).ExportsLazy)
            {
                CUE4Parse.UE4.Assets.Exports.UObject actor;
                try { actor = lazy.Value; } catch { continue; }
                var cls = actor.Class?.Name.Text;
                if (cls is null || actor.Outer?.Name.Text != "PersistentLevel") continue;
                var kind = cls.StartsWith("Button_", StringComparison.Ordinal) ? "buttons"
                    : cls.StartsWith("PowerSocket", StringComparison.Ordinal) ? "power-sockets"
                    : cls.StartsWith("ResourceNode_", StringComparison.Ordinal) ? "resource-nodes"
                    : cls.StartsWith("CharacterCorpse_", StringComparison.Ordinal) ? "corpses"
                    : cls.StartsWith("BP_Teleporter", StringComparison.Ordinal) ? "portals" : null;
                if (kind is null) continue;
                var key = $"/Game/Maps/{map}.{map}:PersistentLevel.{actor.Name}";
                targets.Add(new { kind, cls, region = map, actor = key });
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(targets));
    }

    [Fact]
    public void Inspect_vehicle_recall_links()
    {
        var output = Environment.GetEnvironmentVariable("INSPECTOR_RECALL_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        Assert.NotNull(assets);
        var provider = (CUE4Parse.FileProvider.DefaultFileProvider)typeof(GameAssetProvider)
            .GetField("_provider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(assets)!;
        var lines = new List<string>();
        foreach (var path in assets!.AssetPaths.Where(p => p.Contains("VehicleRecallStation", StringComparison.Ordinal) || p.Contains("CartRecallButton", StringComparison.Ordinal) || p.EndsWith("/Facility.umap", StringComparison.Ordinal)))
        {
            var package = provider.LoadPackage(path);
            foreach (var lazy in package.ExportsLazy)
            {
                var actor = lazy.Value;
                if (path.EndsWith(".umap", StringComparison.Ordinal) && !actor.Name.Contains("Recall", StringComparison.Ordinal)) continue;
                lines.Add(Newtonsoft.Json.JsonConvert.SerializeObject(actor, Newtonsoft.Json.Formatting.Indented));
            }
        }
        File.WriteAllText(output, string.Join('\n', lines));
    }
    [Fact]
    public void Write_posed_corpse_picture_targets()
    {
        var output = Environment.GetEnvironmentVariable("INSPECTOR_LEVEL_PICTURES_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        Assert.NotNull(assets);
        var provider = (CUE4Parse.FileProvider.DefaultFileProvider)typeof(GameAssetProvider)
            .GetField("_provider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(assets)!;
        var targets = new Dictionary<string, List<object>>();
        foreach (var map in new[] { "Facility_Labs_Control", "Facility_DF_War", "Facility_DF_RadWaste", "Facility_Office1", "Facility_Security", "Facility_Pens", "Facility_DarkFusion", "Facility_Labs", "Facility_Dam" })
        {
            var path = assets!.AssetPaths.FirstOrDefault(f => f.EndsWith('/' + map + ".umap", StringComparison.OrdinalIgnoreCase));
            if (path is null) continue;
            var package = provider.LoadPackage(path);
            foreach (var lazy in package.ExportsLazy)
            {
                CUE4Parse.UE4.Assets.Exports.UObject actor;
                try { actor = lazy.Value; } catch { continue; }
                if (!actor.Name.StartsWith("CharacterCorpse_", StringComparison.Ordinal)) continue;
                var cls = actor.Class?.Name.Text;
                if (cls is null) continue;
                if (!targets.TryGetValue(cls, out var candidates)) targets[cls] = candidates = [];
                if (candidates.Count >= 8) continue;
                var key = $"/Game/Maps/{map}.{map}:PersistentLevel.{actor.Name}";
                if (assets.TryGetActorWorldTransform(key) is null) continue;
                candidates.Add(new { kind = "corpses", cls, region = map, actor = key });
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(targets.Values.SelectMany(t => t)));
    }

    [Fact]
    public void Write_inspector_picture_targets()
    {
        var output = Environment.GetEnvironmentVariable("INSPECTOR_PICTURES_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        Assert.NotNull(assets);
        var targets = new List<object>();
        const string content = "AbioticFactor/Content/";
        foreach (var file in assets!.AssetPaths.Where(f => f.StartsWith(content, StringComparison.Ordinal)
                     && f.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var cls = name + "_C";
            var path = "/Game/" + file[content.Length..^7] + "." + cls;
            if (name is "Deployed_Lamp_Sconce" or "Deployed_Lamp_Sconce_XMAS")
                foreach (var on in new[] { false, true })
                    targets.Add(new { kind = "lamp-states", cls = cls + (on ? "_on" : "_off"), classPath = path + (on ? "#lamp=1" : "#lamp=0") });
            if (name.StartsWith("IceWall", StringComparison.Ordinal))
                targets.Add(new { kind = "destructibles", cls, classPath = path });
            if (name is "PowerSocket_ChildOfActor")
                targets.Add(new { kind = "power-sockets", cls, classPath = path });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(targets));
    }

    [Fact]
    public void Inspect_spawner_positions()
    {
        var output = Environment.GetEnvironmentVariable("INSPECTOR_POSITIONS_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        Assert.NotNull(assets);
        var data = WorldSaveReader.ReadFromFile(Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav"));
        var entries = WorldMapAccessor.Entries(data.Raw, "NPCSpawnMap").Where(e => e.Key.Contains("Peccary", StringComparison.Ordinal)).Take(6);
        var provider = (CUE4Parse.FileProvider.DefaultFileProvider)typeof(GameAssetProvider)
            .GetField("_provider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(assets)!;
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            var key = entry.Key;
            var package = provider.LoadPackage(key[..key.IndexOf('.', StringComparison.Ordinal)]);
            var actor = package.GetExportOrNull(key[(key.LastIndexOf('.') + 1)..], StringComparison.OrdinalIgnoreCase);
            lines.Add(key + "\n" + JsonSerializer.Serialize(assets!.TryGetActorWorldTransform(key)));
            lines.Add(string.Join('\n', entry.Props.Select(p => p.Name + " = " + p.Property?.Value)));
            if (actor is not null) lines.Add(Newtonsoft.Json.JsonConvert.SerializeObject(actor, Newtonsoft.Json.Formatting.Indented));
        }
        File.WriteAllText(output, string.Join('\n', lines));
    }
}
