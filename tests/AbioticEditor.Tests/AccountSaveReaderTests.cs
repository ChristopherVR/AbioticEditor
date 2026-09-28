using System.IO;
using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>Read-only account save models (Unlocks / PlayerStatsSave / UserSettings) against the real fixtures.</summary>
public class AccountSaveReaderTests
{
    private static string? Account()
    {
        if (Fixtures.ClientSavedDir is null) return null;
        var dir = Path.Combine(Fixtures.ClientSavedDir, "76561197993781479");
        return Directory.Exists(dir) ? dir : null;
    }

    private static string? Path_(string file)
    {
        var a = Account();
        if (a is null) return null;
        var p = Path.Combine(a, file);
        return File.Exists(p) ? p : null;
    }

    [Theory]
    [InlineData("Unlocks.sav")]
    [InlineData("PlayerStatsSave.sav")]
    [InlineData("UserSettings.sav")]
    public void Account_files_round_trip_byte_exact(string file)
    {
        var path = Path_(file);
        if (path is null) return;
        var original = File.ReadAllBytes(path);
        using var ms = new MemoryStream(original);
        var save = SaveGame.LoadFrom(ms);
        using var outMs = new MemoryStream();
        save.WriteTo(outMs);
        Assert.Equal(original, outMs.ToArray());
    }

    [Fact]
    public void Unlocks_lists_owned_rows_with_categories()
    {
        var path = Path_("Unlocks.sav");
        if (path is null) return;
        var model = AccountSaveReader.ReadUnlocksFile(path);
        Assert.NotEmpty(model.Unlocks);
        Assert.Contains(model.Unlocks, u => u.RowName == "Head_M01chemist" && u.Category == "Head");
        Assert.Contains(model.Unlocks, u => u.RowName == "id_hydro" && u.Category == "ID Card");
        Assert.Contains(model.Unlocks, u => u.RowName == "Tie_Christmas_01" && u.Category == "Tie");
    }

    [Fact]
    public void Unlock_partition_never_offers_rows_outside_the_catalog()
    {
        var model = new CustomizationUnlocksModel([new("Head_A", "Head"), new("Head_Stale", "Head")]);
        var (owned, unavailable) = model.Partition(["Head_A", "Head_B"]);
        Assert.Equal(["Head_A"], owned);
        Assert.Equal(["Head_B"], unavailable);
        Assert.DoesNotContain("Head_Stale", owned.Concat(unavailable));
        Assert.Equal(["Head_Stale"], model.UnknownTo(["Head_A", "Head_B"]));
    }

    [Fact]
    public void PlayerStats_reads_kill_counters_and_achievement_mirror()
    {
        var path = Path_("PlayerStatsSave.sav");
        if (path is null) return;
        var model = AccountSaveReader.ReadPlayerStatsFile(path);
        Assert.NotEmpty(model.Stats);
        Assert.All(model.KillCounters, kv => Assert.StartsWith("STAT_KILLS_", kv.Key));
        Assert.Contains("ACH_CRAFTY", model.Achievements);
        Assert.All(model.Achievements, a => Assert.StartsWith("ACH_", a));
    }

    [Fact]
    public void UserSettings_reads_lists_and_never_exposes_the_host_password()
    {
        var path = Path_("UserSettings.sav");
        if (path is null) return;
        var model = AccountSaveReader.ReadUserSettingsFile(path);
        Assert.Contains("recipe_personalteleporter", model.FavouriteRecipes);
        Assert.Contains("recipe_flamethrower", model.PinnedRecipes);
        Assert.Contains("Tutorial_Sneaking", model.TutorialHintPopupsSeen);
        Assert.NotNull(model.HostPreferences);
        Assert.True(model.HostPreferences!.HasPassword);
        Assert.Contains("SinglePlayer", model.HostPreferences.Flags.Keys);

        // The plaintext password in the fixture must appear nowhere in the model.
        var dump = System.Text.Json.JsonSerializer.Serialize(model);
        Assert.DoesNotContain("pieter123", dump);
    }
}
