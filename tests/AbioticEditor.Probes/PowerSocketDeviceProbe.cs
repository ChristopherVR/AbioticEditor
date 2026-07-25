using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for the "Device not found in any region save" report: power sockets say a
/// device is plugged in, but the folder-wide DeployedObjectMap index cannot name it. Works out
/// where those ids actually live - which map, in which save - so the resolver can look in the
/// right place instead of giving up.
/// </summary>
public class PowerSocketDeviceProbe
{
    private readonly ITestOutputHelper _output;

    public PowerSocketDeviceProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? WorldsDir => Fixtures.ServerWorldsDir;

    [Fact]
    public void Probe_WhereDoPluggedInDeviceIdsLive()
    {
        if (WorldsDir is null) { _output.WriteLine("no fixtures"); return; }
        var saves = Directory.GetFiles(WorldsDir, "WorldSave_*.sav").OrderBy(p => p).ToList();
        _output.WriteLine($"=== {saves.Count} save(s)");

        // Every socket's plugged-in device id, and which save asked.
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Every id that appears as a KEY of any map, and which map/save it came from.
        var keysByMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in saves)
        {
            SaveGame raw;
            try { raw = WorldSaveReader.ReadFromFile(path).Raw; }
            catch (Exception ex) { _output.WriteLine($"{Path.GetFileName(path)}: {ex.Message}"); continue; }
            var file = Path.GetFileName(path);

            foreach (var entry in WorldMapAccessor.Entries(raw, "PowerSocketMap"))
            {
                var device = entry.Props.GetString("PluggedInDeviceAssetID_");
                if (PowerSocketDeviceResolver.IsNothingPlugged(device)) continue;
                wanted.TryAdd(device!, file);
            }

            // Which top-level maps does this save even have?
            foreach (var mapName in WorldSaveMapNames)
            {
                foreach (var entry in WorldMapAccessor.Entries(raw, mapName))
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    if (!keysByMap.TryGetValue(entry.Key, out var where))
                    {
                        keysByMap[entry.Key] = where = new List<string>();
                    }
                    if (where.Count < 4) where.Add($"{mapName}@{file}");
                }
            }
        }

        _output.WriteLine($"=== {wanted.Count} distinct plugged-in device id(s)");
        var found = 0;
        var missing = new List<string>();
        foreach (var (id, askedBy) in wanted.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (keysByMap.TryGetValue(id, out var where))
            {
                found++;
                if (found <= 40) _output.WriteLine($"  FOUND {id} (socket in {askedBy}) -> {string.Join(", ", where)}");
            }
            else
            {
                missing.Add($"  MISSING {id} (socket in {askedBy})");
            }
        }
        _output.WriteLine($"=== resolved {found}, unresolved {missing.Count}");
        foreach (var line in missing.Take(40)) _output.WriteLine(line);

        // Which maps do the resolved ones come from? That is the list the resolver must index.
        var mapHits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, _) in wanted)
        {
            if (!keysByMap.TryGetValue(id, out var where)) continue;
            foreach (var w in where.Select(x => x.Split('@')[0]).Distinct(StringComparer.Ordinal))
            {
                mapHits[w] = mapHits.GetValueOrDefault(w) + 1;
            }
        }
        _output.WriteLine("=== maps that contain plugged-in device ids");
        foreach (var kv in mapHits) _output.WriteLine($"  {kv.Key} x{kv.Value}");
    }

    /// <summary>Top-level GUID-keyed maps a world save can carry, from the schema doc.</summary>
    private static readonly string[] WorldSaveMapNames =
    {
        "DeployedObjectMap", "ContainerMap", "SimpleDoorMap", "SecurityDoorMap", "PowerSocketMap",
        "ButtonMap", "ElevatorMap", "TriggerMap", "ResourceNodeMap", "NPCSpawnMap", "PetNPCMap",
        "VehicleMap", "PortalMap", "TeleporterPadMap", "TramMap", "WorldTeleporterMap",
        "DroppedItemMap", "BaseMap", "LiquidMap", "CraftingBenchMap", "WorldObjectMap",
    };
}
