using System.IO;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Radiation and body temperature (two more members of <c>CharacterStatsSave_Struct</c>, omitted
/// at 0 like the other stats) and adding active effects to a save.
/// </summary>
public class PlayerConditionsAndEffectsWriteTests
{
    private const string ColdPlayer = "Player_76561198179787042.sav";
    private const string WarmPlayer = "Player_76561198144558108.sav";
    private const string HealthyPlayer = "Player_76561197993781479.sav";

    private static string LegacyPlayer(string file) => Path.Combine(Fixtures.CascadeDir!, "PlayerData", file);

    private static string? IrradiatedPlayer() => Fixtures.ClientSavedDir is null ? null : Path.Combine(
        Fixtures.ClientSavedDir, "76561197993781479", "Worlds", "Cascade", "PlayerData", "Player_76561198128277890.sav");

    private static PlayerSaveData RoundTrip(PlayerSaveData save)
    {
        using var ms = new MemoryStream();
        save.Raw.WriteTo(ms);
        ms.Position = 0;
        return PlayerSaveReader.ReadFromStream(ms);
    }

    [SkippableFact]
    public void Radiation_and_body_temperature_are_read_and_default_to_zero()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var irradiated = IrradiatedPlayer();
        Skip.If(irradiated is null || !File.Exists(irradiated), "the Steam client fixture is not in this checkout");

        Assert.Equal(15.76, PlayerSaveReader.ReadFromFile(irradiated!).Stats.Radiation, 2);
        Assert.Equal(-0.51, PlayerSaveReader.ReadFromFile(LegacyPlayer(ColdPlayer)).Stats.BodyTemperature, 2);
        Assert.Equal(1.0, PlayerSaveReader.ReadFromFile(LegacyPlayer(WarmPlayer)).Stats.BodyTemperature, 2);

        var healthy = PlayerSaveReader.ReadFromFile(LegacyPlayer(HealthyPlayer));
        Assert.Equal(0, healthy.Stats.Radiation);
        Assert.Equal(0, healthy.Stats.BodyTemperature);
    }

    [SkippableFact]
    public void Missing_radiation_and_temperature_tags_are_created_and_zero_leaves_the_save_unchanged()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var path = LegacyPlayer(HealthyPlayer);
        var originalLength = new FileInfo(path).Length;

        var untouched = PlayerSaveReader.ReadFromFile(path);
        PlayerSaveWriter.ApplyStats(untouched, untouched.Stats);
        using (var ms = new MemoryStream())
        {
            untouched.Raw.WriteTo(ms);
            Assert.Equal(originalLength, ms.Length);
        }

        var save = PlayerSaveReader.ReadFromFile(path);
        PlayerSaveWriter.ApplyStats(save, save.Stats with { Radiation = 42.5, BodyTemperature = -2 });
        var reread = RoundTrip(save);
        Assert.Equal(42.5, reread.Stats.Radiation);
        Assert.Equal(-2, reread.Stats.BodyTemperature);
    }

    [SkippableFact]
    public void Clearing_radiation_writes_zero_over_the_saved_value()
    {
        var irradiated = IrradiatedPlayer();
        Skip.If(irradiated is null || !File.Exists(irradiated), "the Steam client fixture is not in this checkout");

        var save = PlayerSaveReader.ReadFromFile(irradiated!);
        PlayerSaveWriter.ApplyStats(save, save.Stats with { Radiation = 0 });
        Assert.Equal(0, RoundTrip(save).Stats.Radiation);
    }

    [SkippableFact]
    public void An_effect_added_to_a_character_without_any_creates_the_list()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var save = PlayerSaveReader.ReadFromFile(LegacyPlayer(HealthyPlayer));
        Assert.Empty(save.ActiveBuffs);

        Assert.True(PlayerSaveWriter.AddActiveBuffs(save, [new ActiveBuff("Buff_Caffeinated", null, 300)]));
        var reread = RoundTrip(save);

        var buff = Assert.Single(reread.ActiveBuffs);
        Assert.Equal("Buff_Caffeinated", buff.BuffRow);
        Assert.Equal("EBodyLimbs::AllBones", buff.ParentLimb);
        Assert.Equal(300f, buff.ExpireTime);
    }

    [SkippableFact]
    public void An_effect_added_beside_an_existing_one_keeps_the_original_untouched()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var save = PlayerSaveReader.ReadFromFile(LegacyPlayer(ColdPlayer));
        var original = Assert.Single(save.ActiveBuffs);

        Assert.True(PlayerSaveWriter.AddActiveBuffs(save, [new ActiveBuff("Debuff_Stinky", "EBodyLimbs::AllBones", -1)]));
        var reread = RoundTrip(save);

        Assert.Equal([original, new ActiveBuff("Debuff_Stinky", "EBodyLimbs::AllBones", -1)], reread.ActiveBuffs);

        // Removing the added one again restores the original file byte for byte.
        PlayerSaveWriter.ApplyActiveBuffs(reread, [original]);
        using var restored = new MemoryStream();
        reread.Raw.WriteTo(restored);
        Assert.Equal(new FileInfo(LegacyPlayer(ColdPlayer)).Length, restored.Length);
    }

    [Fact]
    public void A_native_save_gets_its_own_ActiveBuffs_list_and_plain_stat_names()
    {
        var save = PlayerSaveReader.ReadFromFile(CommunityUpdateFiveTests.Fixture("PlayerData/Player_76561197993781479.sav"));
        Assert.Empty(save.ActiveBuffs);

        Assert.True(PlayerSaveWriter.AddActiveBuffs(save, [new ActiveBuff("Debuff_Stinky", null, -1)]));
        PlayerSaveWriter.ApplyStats(save, save.Stats with { Radiation = 7, BodyTemperature = 2 });
        using (var ms = new MemoryStream())
        {
            save.Raw.WriteTo(ms);
            var bytes = ms.ToArray();
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            Assert.Contains("ActiveBuffs", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CurrentBuffDebuffs_", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Radiation_16_", text, StringComparison.Ordinal);
        }
        var reread = RoundTrip(save);
        Assert.Equal([new ActiveBuff("Debuff_Stinky", "EBodyLimbs::AllBones", -1)], reread.ActiveBuffs);
        Assert.Equal(7, reread.Stats.Radiation);
        Assert.Equal(2, reread.Stats.BodyTemperature);
    }

    [SkippableFact]
    public void Adding_nothing_changes_nothing()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var save = PlayerSaveReader.ReadFromFile(LegacyPlayer(HealthyPlayer));
        Assert.False(PlayerSaveWriter.AddActiveBuffs(save, []));
        Assert.False(PlayerSaveWriter.AddActiveBuffs(save, [new ActiveBuff(" ", null, 10)]));
        Assert.Empty(RoundTrip(save).ActiveBuffs);
    }
}
