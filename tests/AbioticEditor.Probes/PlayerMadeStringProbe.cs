using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe: which items actually carry a player-given name? The slot editor offers a
/// CUSTOM NAME box on every slot, and this works out whether the game only ever writes one for
/// a specific family of items (signs, gravestones, name tags) or genuinely for anything.
/// </summary>
public class PlayerMadeStringProbe
{
    private readonly ITestOutputHelper _output;

    public PlayerMadeStringProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_WhichItemsCarryAPlayerMadeName()
    {
        var dir = Fixtures.ClientSavedDir ?? Fixtures.CascadeDir;
        if (dir is null) { _output.WriteLine("no fixtures"); return; }

        var root = Directory.GetParent(dir)?.FullName ?? dir;
        var players = Directory.GetFiles(root, "Player_*.sav", SearchOption.AllDirectories).ToList();
        _output.WriteLine($"=== {players.Count} player save(s) under {root}");

        var named = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<string>();
        var totalSlots = 0;

        foreach (var path in players)
        {
            PlayerSaveData data;
            try { data = PlayerSaveReader.ReadFromFile(path); }
            catch (Exception ex) { _output.WriteLine($"{Path.GetFileName(path)}: {ex.Message}"); continue; }

            foreach (var slot in data.Inventory.Equipment.Concat(data.Inventory.Hotbar).Concat(data.Inventory.Main))
            {
                if (slot.IsEmpty) continue;
                totalSlots++;
                if (string.IsNullOrWhiteSpace(slot.PlayerMadeString)) continue;
                var id = slot.ItemId ?? "(none)";
                named[id] = named.GetValueOrDefault(id) + 1;
                if (samples.Count < 30)
                {
                    samples.Add($"  {Path.GetFileName(path)} [{id}] = \"{slot.PlayerMadeString}\"");
                }
            }
        }

        _output.WriteLine($"=== {totalSlots} non-empty slot(s); {named.Values.Sum()} carry a player-made name");
        foreach (var line in samples) _output.WriteLine(line);
        _output.WriteLine("=== item ids that carry one");
        foreach (var kv in named) _output.WriteLine($"  {kv.Key} x{kv.Value}");
    }
}
