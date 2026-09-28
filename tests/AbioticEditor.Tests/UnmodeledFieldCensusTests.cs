using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Sweeps every fixture save and records which properties the editor's readers do not consume.
/// The printed report is the source of the census table in
/// docs/reference/research/world-and-placed-object-state.md; the assertions pin the headline findings so a
/// reader change (or a new fixture) that alters them is noticed and the doc is refreshed.
/// </summary>
public sealed class UnmodeledFieldCensusTests(ITestOutputHelper output)
{
    private static IReadOnlyList<CensusRow> BuildRows()
    {
        var saves = new List<(CensusSaveKind, UeSaveGame.SaveGame)>();
        foreach (var (_, d) in AllFixtureSaves.RegionSaves) saves.Add((CensusSaveKind.Region, d.Raw));
        foreach (var (_, d) in AllFixtureSaves.MetadataSaves) saves.Add((CensusSaveKind.Metadata, d.Raw));
        foreach (var (_, d) in AllFixtureSaves.PlayerSaves()) saves.Add((CensusSaveKind.Player, d.Raw));
        return UnmodeledFieldCensus.Collect(saves);
    }

    [Fact]
    public void Normalize_strips_the_blueprint_hash_suffix_only()
    {
        Assert.Equal("Hunger", UnmodeledFieldCensus.Normalize("Hunger_2_A6C5CC6E0123456789ABCDEF01234567"));
        Assert.Equal("DeployedObjectMap", UnmodeledFieldCensus.Normalize("DeployedObjectMap"));
        Assert.Equal("Foo_2_short", UnmodeledFieldCensus.Normalize("Foo_2_short"));
    }

    [Fact]
    public void Census_reports_unmodeled_properties_per_save_kind()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        var rows = BuildRows();
        Assert.NotEmpty(rows);

        foreach (var g in rows.GroupBy(r => r.Kind))
        {
            output.WriteLine($"=== {g.Key} ===");
            foreach (var r in g)
            {
                output.WriteLine($"{(r.Modeled ? "modeled  " : "UNMODELED")} {r.Property} : {r.TypeName}  saves={r.SaveCount} elements={r.ElementCount}");
            }
        }

        // Every world save we hold parses to a top-level property list, and the modeled/unmodeled
        // split is total: a row is one or the other.
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Property)));
    }

    [Fact]
    public void Census_shows_no_top_level_UserEntitlements_map_in_any_fixture()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        // "UserEntitlements" only ever appears as the struct TYPE of a ServerEntitlements value;
        // there is no per-player recipe-token map in any fixture (see the research doc).
        Assert.DoesNotContain(BuildRows(), r => r.Property.StartsWith("UserEntitlements", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaf_census_lists_members_of_struct_valued_maps()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        var leaves = UnmodeledFieldCensus.CollectLeaves(AllFixtureSaves.WorldSaves.Select(w => w.Data.Raw));
        Assert.Contains(leaves, l => l.Map == "NarrativeNPCMap" && l.Leaf == "IsDead");
        foreach (var l in leaves)
        {
            output.WriteLine($"{l.Map}.{l.Leaf} : {l.TypeName} x{l.EntryCount}");
        }
    }
}
