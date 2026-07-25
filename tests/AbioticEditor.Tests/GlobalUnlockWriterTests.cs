using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// The world-wide discovery lists (items seen, e-mails read, journal pages, compendium) live in
/// a <c>GlobalUnlocks</c> struct the game delta-serializes away entirely until something is
/// actually unlocked. Writing one therefore has to be able to create the struct and the
/// individual arrays; before this was fixed, saving a world that had never unlocked anything
/// threw "Could not apply world unlock" and lost the whole save, and a world missing only the
/// compendium arrays dropped those edits silently.
/// </summary>
public class GlobalUnlockWriterTests
{
    private static readonly string[] TwoValues = ["one", "two"];
    private static readonly string[] OnePage = ["page_one"];

    private static readonly string[] Prefixes =
    [
        "GlobalItemsPickedUp_", "GlobalEmailsRead_", "GlobalJournalEntries_",
        "GlobalCompendiumEmail_", "GlobalCompendiumNarrative_", "GlobalCompendiumExploration_",
    ];

    private static string? MetadataSave => Fixtures.ServerWorldsDir is { } dir
        ? Path.Combine(dir, "WorldSave_MetaData.sav")
        : null;

    [Fact]
    public void Writes_every_unlock_list_and_reads_it_back()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;

        foreach (var prefix in Prefixes)
        {
            var data = WorldSaveReader.ReadFromFile(path);
            var values = new[] { "alpha", "beta", "gamma" };

            Assert.True(WorldSaveWriter.ApplyGlobalUnlockArray(data, prefix, values),
                $"{prefix} should be writable");

            var written = WorldSaveReader.ReadGlobalUnlockArray(data.Raw, prefix);
            Assert.Equal(values, written);
        }
    }

    /// <summary>
    /// The failing case from the field: a world that has never unlocked anything has no
    /// GlobalUnlocks struct at all, so the writer has to mint it rather than refuse.
    /// </summary>
    [Fact]
    public void Creates_the_struct_on_a_world_that_has_never_unlocked_anything()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;

        var data = WorldSaveReader.ReadFromFile(path);
        // Strip the struct to reproduce a save that never recorded an unlock.
        var existing = data.Raw.Properties?.FirstOrDefault(p => p.Name.Value.StartsWith("GlobalUnlocks", StringComparison.Ordinal));
        if (existing is not null) data.Raw.Properties!.Remove(existing);
        Assert.Empty(WorldSaveReader.ReadGlobalUnlockArray(data.Raw, "GlobalItemsPickedUp_"));

        Assert.True(WorldSaveWriter.ApplyGlobalUnlockArray(data, "GlobalItemsPickedUp_", TwoValues));
        Assert.Equal(TwoValues, WorldSaveReader.ReadGlobalUnlockArray(data.Raw, "GlobalItemsPickedUp_"));
    }

    /// <summary>A created struct must survive a real write/read round trip, not just in memory.</summary>
    [Fact]
    public void Created_struct_survives_a_round_trip_through_disk()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;

        var scratch = Directory.CreateTempSubdirectory("abiotic-globalunlocks-");
        try
        {
            var copy = Path.Combine(scratch.FullName, "WorldSave_MetaData.sav");
            File.Copy(path, copy);

            var data = WorldSaveReader.ReadFromFile(copy);
            var existing = data.Raw.Properties?.FirstOrDefault(p => p.Name.Value.StartsWith("GlobalUnlocks", StringComparison.Ordinal));
            if (existing is not null) data.Raw.Properties!.Remove(existing);

            Assert.True(WorldSaveWriter.ApplyGlobalUnlockArray(data, "GlobalCompendiumNarrative_", OnePage));
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy);
            Assert.Equal(OnePage, WorldSaveReader.ReadGlobalUnlockArray(reloaded.Raw, "GlobalCompendiumNarrative_"));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_prefix_is_refused_rather_than_silently_dropped()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;
        var data = WorldSaveReader.ReadFromFile(path);
        Assert.False(WorldSaveWriter.ApplyGlobalUnlockArray(data, "GlobalSomethingElse_", OnePage));
    }
}
