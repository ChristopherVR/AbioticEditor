using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using Xunit;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Every in-place writer handed back exactly what the reader produced must leave the file
/// byte-identical. The app's SAVE re-applies every section on every save, so a writer that
/// "normalizes" anything (retargets an empty slot's data table, creates a tag for a value the
/// game deliberately omitted) rewrites saves the player never asked to change - the class of
/// bug behind Nexus report #1, where any edit at all changed how the character loaded.
/// Writers that deliberately re-sort a string array (recipes, codex lists) are covered by
/// <see cref="PlayerSaveSessionRoundTripTests"/> as multisets instead.
/// </summary>
public sealed class PlayerSaveWriterByteImpactTests(ITestOutputHelper output)
{
    [Fact]
    public void In_place_writers_given_unchanged_values_are_byte_identical()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            var original = File.ReadAllBytes(path);
            output.WriteLine($"== {Path.GetFileName(path)} ({original.Length} bytes)");
            var steps = new (string Name, Action<PlayerSaveData> Apply)[]
            {
                ("ApplyStats", d => PlayerSaveWriter.ApplyStats(d, d.Stats)),
                ("ApplyLimbHealth", d => PlayerSaveWriter.ApplyLimbHealth(d, d.Health)),
                ("ApplySkills", d => PlayerSaveWriter.ApplySkills(d, d.Skills)),
                ("ApplyTraits", d => PlayerSaveWriter.ApplyTraits(d, d.Traits)),
                ("ApplyItemsPickedUp", d => PlayerSaveWriter.ApplyItemsPickedUp(d, d.ItemsPickedUp)),
                ("ApplyCraftedItems", d => PlayerSaveWriter.ApplyCraftedItems(d, d.CraftedItems)),
                ("ApplyMapsUnlocked", d => PlayerSaveWriter.ApplyMapsUnlocked(d, d.MapsUnlocked)),
                ("ApplyInventory", d => PlayerSaveWriter.ApplyInventory(d, d.Inventory)),
                ("ApplyTransmogSlots", d => PlayerSaveWriter.ApplyTransmogSlots(d, d.TransmogSlots)),
                ("ApplyTransmogVisibility", d => PlayerSaveWriter.ApplyTransmogVisibility(d, d.TransmogVisibility)),
                ("ApplyEmailsRead", d => PlayerSaveWriter.ApplyEmailsRead(d, d.EmailsRead)),
                ("ApplyJournals", d => PlayerSaveWriter.ApplyJournals(d, d.Journals)),
                ("ApplyFishCaught", d => PlayerSaveWriter.ApplyFishCaught(d, d.FishCaught)),
            };
            foreach (var (name, apply) in steps)
            {
                var data = PlayerSaveReader.ReadFromFile(path);
                try { apply(data); }
                catch (Exception ex) { output.WriteLine($"  {name}: threw {ex.GetType().Name}: {ex.Message}"); continue; }
                using var buffer = new MemoryStream();
                data.Raw.WriteTo(buffer);
                var bytes = buffer.ToArray();
                var first = FirstDiff(original, bytes);
                var line = $"{Path.GetFileName(path)} {name}: {bytes.Length - original.Length:+#;-#;0} bytes, first diff at {(first < 0 ? "none" : first.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
                output.WriteLine(line);
                if (first >= 0) failures.Add(line);
            }
        }
        Assert.Empty(failures);
    }

    private static int FirstDiff(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : n;
    }
}


/// <summary>
/// The survival stats are delta-serialized against a blueprint default of 0 (see
/// PlayerSaveReader.ReadStats). A save missing a stat tag must read as 0, and writing that same
/// 0 back must not invent the tag; writing anything else must.
/// </summary>
public sealed class PlayerSurvivalStatDefaultTests
{
    [Fact]
    public void Missing_stat_tags_read_as_zero_and_round_trip_byte_identical()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        var data = PlayerSaveReader.ReadFromFile(path);
        var stats = StatsStruct(data);
        // Simulate the game's own delta-omission of a rested (0) fatigue and a bottomed-out sanity.
        stats.Remove(stats.Single(t => t.Name.Value!.StartsWith("Fatigue_", StringComparison.Ordinal)));
        stats.Remove(stats.Single(t => t.Name.Value!.StartsWith("Sanity_", StringComparison.Ordinal)));
        using var stripped = new MemoryStream();
        data.Raw.WriteTo(stripped);

        var reread = PlayerSaveReader.ReadFromStream(new MemoryStream(stripped.ToArray()));
        Assert.Equal(0, reread.Stats.Fatigue);
        Assert.Equal(0, reread.Stats.Sanity);
        Assert.NotEqual(0, reread.Stats.Hunger);

        PlayerSaveWriter.ApplyStats(reread, reread.Stats);
        using var unchanged = new MemoryStream();
        reread.Raw.WriteTo(unchanged);
        Assert.Equal(stripped.ToArray(), unchanged.ToArray());

        PlayerSaveWriter.ApplyStats(reread, reread.Stats with { Fatigue = 37.5 });
        var again = PlayerSaveReader.ReadFromStream(Serialize(reread));
        Assert.Equal(37.5, again.Stats.Fatigue);
        Assert.Equal(0, again.Stats.Sanity);
    }

    private static MemoryStream Serialize(PlayerSaveData data)
    {
        var buffer = new MemoryStream();
        data.Raw.WriteTo(buffer);
        buffer.Position = 0;
        return buffer;
    }

    private static IList<UeSaveGame.FPropertyTag> StatsStruct(PlayerSaveData data)
    {
        var rootTag = data.Raw.Properties!.Single(t => t.Name.Value == "CharacterSaveData");
        var root = ((UeSaveGame.StructData.PropertiesStruct)((UeSaveGame.PropertyTypes.StructProperty)rootTag.Property!).Value!).Properties;
        var tag = root.Single(t => t.Name.Value!.StartsWith("CurrentSurvivalStats_", StringComparison.Ordinal));
        var sp = (UeSaveGame.PropertyTypes.StructProperty)tag.Property!;
        return ((UeSaveGame.StructData.PropertiesStruct)sp.Value!).Properties;
    }
}
