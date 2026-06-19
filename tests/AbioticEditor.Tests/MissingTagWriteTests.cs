using System.IO;
using System.Reflection;
using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Tests for the delta-serialization fix: Abiotic Factor omits properties that sit at
/// their blueprint default (e.g. a fresh character's <c>Hunger_</c> is absent from
/// <c>CurrentSurvivalStats_</c>). The reader must fall back to the game default (100)
/// and the writer must create the missing tag rather than silently dropping the edit.
/// </summary>
public class MissingTagWriteTests
{
    private readonly ITestOutputHelper _output;

    public MissingTagWriteTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ---------- local tree helpers (PlayerSaveReader's are internal to Core) ----------

    private static FPropertyTag? FindByPrefix(IEnumerable<FPropertyTag> tags, string prefix)
        => tags.FirstOrDefault(t => t.Name?.Value is { } n && n.StartsWith(prefix, StringComparison.Ordinal));

    private static IList<FPropertyTag> GetCharacterSaveData(SaveGame save)
    {
        var top = FindByPrefix(save.Properties!, "CharacterSaveData")
            ?? throw new InvalidDataException("Player save is missing the CharacterSaveData property.");
        if (top.Property is not StructProperty sp || sp.Value is not PropertiesStruct ps)
            throw new InvalidDataException("CharacterSaveData was not a struct of properties.");
        return ps.Properties;
    }

    private static IList<FPropertyTag>? GetStatsStruct(SaveGame save)
    {
        var statsTag = FindByPrefix(GetCharacterSaveData(save), "CurrentSurvivalStats_");
        return statsTag?.Property is StructProperty sp && sp.Value is PropertiesStruct ps
            ? ps.Properties
            : null;
    }

    /// <summary>
    /// A client-tree (<c>fixtures/SteamSaves/SaveGames</c>) player save whose stats struct lacks a
    /// <c>Hunger_</c> tag (a fresh character at full hunger). Null when the live tree is
    /// absent or every save carries the tag - callers skip in that case.
    /// </summary>
    private static string? FindPlayerSaveWithoutHunger()
    {
        if (NewSaveDeepDiveTests.NewSavesRoot is null) return null;

        foreach (var path in Directory.EnumerateFiles(
                     NewSaveDeepDiveTests.NewSavesRoot, "Player_*.sav", SearchOption.AllDirectories))
        {
            try
            {
                var save = PlayerSaveReader.ReadFromFile(path);
                var stats = GetStatsStruct(save.Raw);
                if (stats is not null && FindByPrefix(stats, "Hunger_") is null)
                {
                    return path;
                }
            }
            catch (IOException)
            {
                // unreadable copy (e.g. locked) - keep scanning
            }
        }
        return null;
    }

    private static string CascadePlayerSavePath => Path.Combine(
        Fixtures.CascadeDir ?? string.Empty, "PlayerData", "Player_76561197993781479.sav");

    // =====================================================================
    // Reader: missing stat tags read as the game default (100), not 0
    // =====================================================================

    [Fact]
    public void Reader_MissingHungerTag_DefaultsTo100()
    {
        var path = FindPlayerSaveWithoutHunger();
        if (path is null)
        {
            _output.WriteLine("SKIP: no live save without a Hunger_ tag.");
            return;
        }
        _output.WriteLine($"Subject: {path}");

        var save = PlayerSaveReader.ReadFromFile(path);

        Assert.Equal(100.0, save.Stats.Hunger);
        // The other stats were explicitly serialized on this save (they had drifted off
        // their defaults) and must not be touched by the defaulting logic.
        Assert.NotEqual(0.0, save.Stats.Thirst);
        Assert.NotEqual(0.0, save.Stats.Continence);
    }

    // =====================================================================
    // Writer: editing a stat whose tag the save omitted must create the tag
    // =====================================================================

    [Fact]
    public void Writer_MissingHungerTag_CreatesTagAndPersists()
    {
        var path = FindPlayerSaveWithoutHunger();
        if (path is null)
        {
            _output.WriteLine("SKIP: no live save without a Hunger_ tag.");
            return;
        }
        _output.WriteLine($"Subject: {path}");

        var originalBytes = File.ReadAllBytes(path);
        var save = PlayerSaveReader.ReadFromFile(path);
        Assert.Null(FindByPrefix(GetStatsStruct(save.Raw)!, "Hunger_"));

        // Edit only Hunger; keep every other stat at its parsed value.
        var stats = save.Stats with { Hunger = 77 };
        PlayerSaveWriter.ApplyStats(save, stats);

        var tmp = Path.Combine(Path.GetTempPath(), $"abiotic-missingtag-{Guid.NewGuid():N}.sav");
        try
        {
            PlayerSaveWriter.WriteToFile(save, tmp);
            var rewrittenBytes = File.ReadAllBytes(tmp);

            // The new Hunger_ tag must have grown the file.
            Assert.True(rewrittenBytes.Length > originalBytes.Length,
                $"expected rewritten save to grow (was {originalBytes.Length}, now {rewrittenBytes.Length})");

            // Re-read through the full typed parser: the file must still parse fully
            // and the edit must have landed.
            var reread = PlayerSaveReader.ReadFromFile(tmp);
            Assert.Equal(77.0, reread.Stats.Hunger);
            Assert.Equal(save.Stats.Thirst, reread.Stats.Thirst);
            Assert.Equal(save.Stats.Sanity, reread.Stats.Sanity);
            Assert.Equal(save.Stats.Fatigue, reread.Stats.Fatigue);
            Assert.Equal(save.Stats.Continence, reread.Stats.Continence);
            Assert.Equal(save.Stats.Money, reread.Stats.Money);
            Assert.Equal(save.Skills.Count, reread.Skills.Count);
            Assert.Equal(save.Inventory.Equipment.Count, reread.Inventory.Equipment.Count);
            Assert.Equal(save.Inventory.Hotbar.Count, reread.Inventory.Hotbar.Count);
            Assert.Equal(save.Inventory.Main.Count, reread.Inventory.Main.Count);

            // The created tag carries the exact blueprint name the game looks up.
            var hungerTag = FindByPrefix(GetStatsStruct(reread.Raw)!, "Hunger_");
            Assert.NotNull(hungerTag);
            Assert.Equal("Hunger_2_A6C5CC6E41993323B119FA9E0B3894CA", hungerTag!.Name.Value);
            Assert.Equal("DoubleProperty", hungerTag.Type.Name.Value);

            // And the rewritten file itself round-trips byte-identical (no latent
            // serialization damage from the inserted tag).
            using var ms = new MemoryStream();
            reread.Raw.WriteTo(ms);
            Assert.True(ms.ToArray().AsSpan().SequenceEqual(rewrittenBytes), "rewritten save no longer round-trips");
        }
        finally
        {
            File.Delete(tmp);
            File.Delete(tmp + ".bak");
        }
    }

    [Fact]
    public void Writer_RemovedHungerAndMoneyTags_AreRecreatedWithSameSize()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        Assert.True(File.Exists(CascadePlayerSavePath));

        var originalLength = new FileInfo(CascadePlayerSavePath).Length;
        var save = PlayerSaveReader.ReadFromFile(CascadePlayerSavePath);
        var originalStats = save.Stats;

        // Simulate delta-serialization: drop the Hunger_ stat and CurrentMoney_ tags.
        var statsStruct = GetStatsStruct(save.Raw)!;
        statsStruct.Remove(FindByPrefix(statsStruct, "Hunger_")!);
        var root = GetCharacterSaveData(save.Raw);
        root.Remove(FindByPrefix(root, "CurrentMoney_")!);

        // Writing the original values back must recreate both tags.
        PlayerSaveWriter.ApplyStats(save, originalStats);

        using var ms = new MemoryStream();
        save.Raw.WriteTo(ms);

        // Identical tags (same names, types, flags, values) => identical total size.
        Assert.Equal(originalLength, ms.Length);

        ms.Position = 0;
        var reread = PlayerSaveReader.ReadFromStream(ms);
        Assert.Equal(originalStats, reread.Stats);
    }

    // =====================================================================
    // Regression: saves that already carry the tags still patch in place
    // =====================================================================

    [Fact]
    public void Writer_ExistingTags_StillRoundTripStatsEdit()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        Assert.True(File.Exists(CascadePlayerSavePath));

        var originalLength = new FileInfo(CascadePlayerSavePath).Length;
        var save = PlayerSaveReader.ReadFromFile(CascadePlayerSavePath);

        var stats = new CharacterStats(Hunger: 77, Thirst: 66, Sanity: 55, Fatigue: 44, Continence: 33, Money: 2222);
        PlayerSaveWriter.ApplyStats(save, stats);

        using var ms = new MemoryStream();
        save.Raw.WriteTo(ms);

        // No tags were added - all five stats plus money already existed.
        Assert.Equal(originalLength, ms.Length);

        ms.Position = 0;
        var reread = PlayerSaveReader.ReadFromStream(ms);
        Assert.Equal(stats, reread.Stats);
    }

    // =====================================================================
    // Probe (documentation): exact tag metadata the writer's FullNames came from
    // =====================================================================

    [Fact]
    public void Probe_DumpStatsTagMetadata()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        Assert.True(File.Exists(CascadePlayerSavePath));

        var save = PlayerSaveReader.ReadFromFile(CascadePlayerSavePath);
        var root = GetCharacterSaveData(save.Raw);

        var flagsProp = typeof(FPropertyTag).GetProperty("Flags", BindingFlags.NonPublic | BindingFlags.Instance)!;

        void Dump(FPropertyTag t, string indent = "")
        {
            var flags = flagsProp.GetValue(t);
            var typeParams = string.Join(",", t.Type.Parameters.Select(p => p.Name.Value));
            _output.WriteLine($"{indent}{t.Name.Value}  type={t.Type.Name.Value}({typeParams})  flags={flags}  prop={t.Property?.GetType().Name}  value={t.Property?.Value}");
        }

        var statsTag = FindByPrefix(root, "CurrentSurvivalStats_")!;
        Dump(statsTag);
        foreach (var t in GetStatsStruct(save.Raw)!) Dump(t, "  ");
        Dump(FindByPrefix(root, "CurrentMoney_")!);

        // Chrissie (fresh character, newest build): which stats tags exist + values.
        var path = FindPlayerSaveWithoutHunger();
        if (path is null) { _output.WriteLine("no live save without Hunger_"); return; }
        _output.WriteLine($"--- {path}");
        var save2 = PlayerSaveReader.ReadFromFile(path);
        var root2 = GetCharacterSaveData(save2.Raw);
        Dump(FindByPrefix(root2, "CurrentSurvivalStats_")!);
        foreach (var t in GetStatsStruct(save2.Raw)!) Dump(t, "  ");
        var money2 = FindByPrefix(root2, "CurrentMoney_");
        if (money2 is null) _output.WriteLine("CurrentMoney_ ABSENT");
        else Dump(money2);
    }
}
