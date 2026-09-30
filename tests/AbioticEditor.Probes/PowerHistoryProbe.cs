using System.IO;
using System.Linq;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Power evidence from the game's own save history: the game keeps rolling snapshots of a world
/// (<c>SaveGames/&lt;id&gt;/Backups/&lt;World&gt;/1..5</c>), each written by the game, so consecutive
/// snapshots are natural before/after pairs around whatever the player did in between (plugging,
/// unplugging, placing and packaging power devices). Copy the snapshots into a folder laid out as
/// <c>&lt;root&gt;/&lt;World&gt;/&lt;n&gt;/WorldSave_Facility.sav</c> and point
/// <c>ABIOTIC_POWER_HISTORY</c> at the root. Output-only.
/// </summary>
public class PowerHistoryProbe
{
    private readonly ITestOutputHelper _output;
    public PowerHistoryProbe(ITestOutputHelper output) => _output = output;

    private sealed record Socket(string Id, string Plugged, string[] Extras);

    private static Dictionary<string, Socket> Sockets(SaveGame save)
    {
        var result = new Dictionary<string, Socket>(StringComparer.Ordinal);
        foreach (var e in WorldMapAccessor.Entries(save, "PowerSocketMap"))
        {
            var plugged = e.Props.FindByPrefix("PluggedInDeviceAssetID_")?.Property?.Value is FString s ? s.Value ?? "-1" : "(absent)";
            var extras = e.Props.FindByPrefix("ExtraPoweredDeviceAssetIDs_")?.Property is ArrayProperty arr
                ? arr.Value?.OfType<FProperty>().Select(p => p.Value is FString f ? f.Value ?? "" : p.Value?.ToString() ?? "").ToArray() ?? []
                : [];
            result[e.Key] = new Socket(e.Key, plugged, extras);
        }
        return result;
    }

    private static Dictionary<string, string> Deployables(WorldSaveData data)
        => (PlacedObjectCensus.Build(data).Objects ?? [])
            .GroupBy(o => o.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ClassName ?? "?", StringComparer.Ordinal);

    [Fact]
    public void Dump_PowerChangesBetweenGameSnapshots()
    {
        var root = Environment.GetEnvironmentVariable("ABIOTIC_POWER_HISTORY");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { _output.WriteLine("set ABIOTIC_POWER_HISTORY"); return; }
        foreach (var world in Directory.GetDirectories(root))
        {
            var files = Directory.GetFiles(world, "WorldSave_Facility.sav", SearchOption.AllDirectories)
                .OrderBy(File.GetLastWriteTimeUtc).ToList();
            _output.WriteLine($"\n######## {Path.GetFileName(world)}: {files.Count} snapshots");
            (Dictionary<string, Socket> S, Dictionary<string, string> D, string Name)? prev = null;
            foreach (var f in files)
            {
                var data = WorldSaveReader.ReadFromFile(f);
                var cur = (Sockets(data.Raw), Deployables(data), $"{Path.GetFileName(Path.GetDirectoryName(f))} @ {File.GetLastWriteTime(f):MM-dd HH:mm}");
                _output.WriteLine($"-- {cur.Item3}: {cur.Item1.Count} sockets, {cur.Item1.Values.Count(s => s.Plugged is not "-1" and not "(absent)")} plugged, {cur.Item1.Values.Count(s => s.Extras.Length > 0)} with extras, {cur.Item2.Count} deployables");
                if (prev is { } p)
                {
                    string Name(string key, Dictionary<string, string> d1, Dictionary<string, string> d2)
                        => key is "-1" or "(absent)" ? key : $"{(d2.TryGetValue(key, out var c) ? c : d1.TryGetValue(key, out var c2) ? c2 + " [gone]" : "not placed")}({key[..Math.Min(8, key.Length)]})";
                    foreach (var (id, s) in cur.Item1)
                    {
                        var owner = id.Length == 33 ? id[..32] : null;
                        var ownerText = owner is null ? "level socket" : Name(owner, p.D, cur.Item2);
                        if (!p.S.TryGetValue(id, out var before))
                        {
                            var ownerNew = owner is not null && !p.D.ContainsKey(owner) ? " (owner newly placed)" : owner is not null ? " (owner existed before!)" : "";
                            _output.WriteLine($"   + socket {id[..Math.Min(10, id.Length)]}.. of {ownerText}{ownerNew}: plugged {Name(s.Plugged, p.D, cur.Item2)}");
                            continue;
                        }
                        if (before.Plugged != s.Plugged)
                            _output.WriteLine($"   ~ socket {id[..Math.Min(10, id.Length)]}.. of {ownerText}: {Name(before.Plugged, p.D, cur.Item2)} -> {Name(s.Plugged, p.D, cur.Item2)}");
                        if (!before.Extras.SequenceEqual(s.Extras))
                            _output.WriteLine($"   ~ extras {id[..Math.Min(10, id.Length)]}..: [{string.Join(",", before.Extras)}] -> [{string.Join(",", s.Extras)}]");
                    }
                    foreach (var (id, s) in p.S.Where(kv => !cur.Item1.ContainsKey(kv.Key)))
                    {
                        var owner = id.Length == 33 ? id[..32] : null;
                        var gone = owner is not null && !cur.Item2.ContainsKey(owner) ? " (owner removed too)" : owner is not null ? " (owner still placed!)" : "";
                        _output.WriteLine($"   - socket {id[..Math.Min(10, id.Length)]}.. of {(owner is null ? "level socket" : Name(owner, p.D, cur.Item2))}{gone}: was plugged {Name(s.Plugged, p.D, cur.Item2)}");
                    }
                    // Devices that moved between snapshots while plugged (does a plug survive a move?).
                    var addedPower = cur.Item2.Keys.Except(p.D.Keys).Count(k => cur.Item2[k].Contains("Plug", StringComparison.Ordinal) || cur.Item2[k].Contains("Battery", StringComparison.Ordinal) || cur.Item2[k].Contains("Cable", StringComparison.Ordinal));
                    var removedPower = p.D.Keys.Except(cur.Item2.Keys).Count(k => p.D[k].Contains("Plug", StringComparison.Ordinal) || p.D[k].Contains("Battery", StringComparison.Ordinal) || p.D[k].Contains("Cable", StringComparison.Ordinal));
                    _output.WriteLine($"   power devices placed {addedPower}, removed {removedPower}");
                }
                prev = cur;
            }
        }
    }
}
