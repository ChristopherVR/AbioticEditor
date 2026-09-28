using System.Text;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Tests;

/// <summary>
/// Fixture census for the "deployables, pets and progression" gaps. These pin what the saves
/// actually contain (and what they do not), so the research note in
/// <c>docs/reference/research/research-garden-planting-and-pet-feeding.md</c> stays checkable.
/// </summary>
public sealed class ProgressionCensusFixtureTests
{
    private static IEnumerable<string> WorldFolders()
    {
        var folders = new List<string>();
        if (Fixtures.ServerWorldsDir is { } server) folders.Add(server);
        if (Fixtures.CascadeDir is { } cascade) folders.Add(cascade);
        folders.AddRange(Fixtures.ClientWorldSaves("WorldSave_MetaData.sav").Select(p => Path.GetDirectoryName(p)!));
        return folders.Distinct();
    }

    private static IEnumerable<string> WorldSaves(string pattern)
        => WorldFolders().SelectMany(d => Directory.EnumerateFiles(d, pattern)).Order(StringComparer.Ordinal);

    [Fact]
    public void Chemistry_bench_entries_carry_no_bench_specific_or_timer_fields()
    {
        // A bench is a generic deployed-object struct: the same top-level tags as a garden plot or
        // a chair. Mixing progress (ProcessingActive / ProcessingTimestamp) is not among them, and
        // the words appear nowhere in the region saves, so the timers are runtime-only.
        string[] genericTags =
        [
            "Class_", "ActorPath_", "ChangableData_", "DeployableDestroyed_", "BrokeWhenPackaged_", "HasBeenPackaged_",
            "Transform_", "DeployedByPlayer_", "ConstructionMode_", "ConstructionLevel_", "ContainerInventories_",
            "ActiveSeats_", "ItemProxies_", "CustomTextDisplay_", "FoundByPlayer_", "Supports_", "NoResetVignette_",
            "CustomSpawnedTime_",
        ];
        var benches = 0;
        foreach (var path in WorldSaves("WorldSave_Facility.sav"))
        {
            var save = WorldSaveReader.ReadFromFile(path).Raw;
            foreach (var bench in new ChemistryBenchesFeature().Read(save))
            {
                benches++;
                var props = WorldMapAccessor.FindEntry(save, "DeployedObjectMap", bench.Key)!;
                Assert.Equal(genericTags.Length, props.Count);
                Assert.All(props, t => Assert.Contains(genericTags, g => t.Name!.Value.StartsWith(g, StringComparison.Ordinal)));
                Assert.Equal("Available only while the game is running", bench.Fields.Single(f => f.Id == "processing").Value);
            }
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.AsSpan().IndexOf("ProcessingActive"u8) < 0);
            Assert.True(bytes.AsSpan().IndexOf("ProcessingTimestamp"u8) < 0);
        }
        if (benches == 0) return;
        Assert.True(benches >= 3);
    }

    [Fact]
    public void Every_fixture_bench_slot_is_empty_and_saved_flasks_are_plain_items()
    {
        var flasks = 0;
        foreach (var path in WorldSaves("WorldSave_Facility.sav"))
        {
            var save = WorldSaveReader.ReadFromFile(path).Raw;
            foreach (var bench in new ChemistryBenchesFeature().Read(save))
                Assert.All(bench.Fields.Where(f => f.Id.StartsWith("flask:", StringComparison.Ordinal)), f => Assert.Equal("Empty", f.Value));
            foreach (var container in WorldSaveReader.ReadFromFile(path).Containers)
                foreach (var slot in container.Inventories.SelectMany(i => i.Slots).Where(s => s.ItemId?.StartsWith("flask_", StringComparison.Ordinal) == true))
                {
                    flasks++;
                    Assert.Equal("flask_pheromone", slot.ItemId);
                    Assert.True(slot.LiquidLevel <= 0);
                }
        }
        if (flasks > 0) Assert.True(flasks >= 3);
    }

    [Fact]
    public void Saved_narrative_phases_are_only_zero_two_and_three_and_only_dead_entries_leave_three()
    {
        var names = GameDataRegistry.LoadBundled()?.NarrativeNpcNames;
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var deadPhases = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var total = 0;
        var named = 0;
        foreach (var path in WorldSaves("WorldSave_*.sav"))
        {
            foreach (var npc in WorldSaveReader.ReadFromFile(path).Npcs.Where(n => !n.IsPet))
            {
                total++;
                var phase = npc.State![^1..];
                seen.Add(phase);
                var name = NarrativeNpcNameCatalog.Resolve(names, npc.Id);
                if (name is not null) named++;
                if (phase != "3") Assert.True(npc.IsDead, $"{npc.Id} left phase 3 while alive");
                if (npc.IsDead && name is not null)
                {
                    if (!deadPhases.TryGetValue(name, out var set)) deadPhases[name] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(phase);
                }
            }
        }
        if (total == 0) return;
        Assert.Equal(["0", "2", "3"], seen);
        // Ela (Abe's Electro-Pest) is the only character seen in phase 0, Abe and Dr. Jager in phase 2.
        Assert.Equal(["0"], deadPhases["Ela"]);
        Assert.Contains("2", deadPhases["Abe"]);
        Assert.Contains("2", deadPhases["Dr. Jager"]);
        // Phases 1, 4 and 5 exist in the enum but were never saved, so no label could be verified.
        Assert.DoesNotContain(seen, p => p is "1" or "4" or "5");
        if (names is { Count: > 0 }) Assert.True(named * 100 / total >= 90, "most saved story characters resolve to a verified name");
    }

    [Fact]
    public void No_summoned_companion_appears_in_any_saved_pet_map_or_region_save()
    {
        // Every PetNPC entry across every fixture world is a tamed base pet. The armor-set summon
        // classes (NPC_Exor_Ally, NPC_MageEye_Ally) never occur, in the pet map or anywhere in the
        // Facility region saves' raw text.
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in WorldSaves("WorldSave_*.sav"))
            foreach (var pet in PetSavedCareState.ReadWorld(WorldSaveReader.ReadFromFile(path).Raw))
                classes.Add(pet.Species ?? "");
        if (classes.Count == 0) return;
        Assert.Equal(["NPC_Monster_Pest_Electro", "NPC_Peccary_Sow", "NPC_Skink_Crafted"], classes);
        Assert.All(classes, c => Assert.False(PetCatalog.IsSummon(c)));
        foreach (var path in WorldSaves("WorldSave_Facility.sav"))
        {
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.AsSpan().IndexOf("Exor_Ally"u8) < 0);
            Assert.True(bytes.AsSpan().IndexOf("MageEye_Ally"u8) < 0);
            Assert.True(bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("Summon")) < 0);
        }
    }
}
