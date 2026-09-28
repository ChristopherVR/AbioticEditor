using System.IO;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Read-only modeling (plus two proven-shape writers) for the character-save properties
/// <c>TransmogDisabledArray_</c>, <c>FavoritedSlots_</c>, <c>ItemsDistilled_</c>,
/// <c>CurrentBuffDebuffs_</c> and <c>LastHotbarSelection_</c>. Expectations come from the
/// fixture survey in docs/reference/research/research-player-slot-flags-and-effects.md.
/// </summary>
public class PlayerSlotFlagsAndEffectsTests
{
    private const string Player = "Player_76561197993781479.sav";
    private const string BuffPlayer = "Player_76561198179787042.sav";

    private static IEnumerable<string> AllPlayerSaves()
    {
        var roots = new[] { Fixtures.CascadeDir, Fixtures.ClientSavedDir, Fixtures.ServerWorldsDir }
            .Where(r => r is not null && Directory.Exists(r));
        return roots.SelectMany(r => Directory.EnumerateFiles(r!, "Player_*.sav", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);
    }

    private static string? ChrissiePlayer()
        => Fixtures.ClientSavedDir is null ? null
            : Directory.EnumerateFiles(Fixtures.ClientSavedDir, Player, SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Replace('\\', '/').Contains("/Chrissie/", StringComparison.Ordinal));

    private static PlayerSaveData Reload(PlayerSaveData data)
    {
        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        return PlayerSaveReader.ReadFromStream(ms);
    }

    [SkippableFact]
    public void Every_fixture_player_carries_a_13_flag_transmog_array_and_matching_shapes()
    {
        var saves = AllPlayerSaves().ToList();
        Skip.If(saves.Count == 0, "no player fixtures in this checkout");

        foreach (var path in saves)
        {
            var data = PlayerSaveReader.ReadFromFile(path);
            Assert.Equal(13, data.TransmogDisabled.Count);
            // Same length as the equipment array, unlike the 6 transmog slots / 12 visibility flags.
            Assert.Equal(data.Inventory.Equipment.Count, data.TransmogDisabled.Count);
            Assert.Equal(12, data.TransmogVisibility.Count);
        }
    }

    [SkippableFact]
    public void Favorited_slots_and_distilled_history_are_read_from_every_older_format_save()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir!, "Player_*.sav", SearchOption.AllDirectories))
        {
            var data = PlayerSaveReader.ReadFromFile(path);
            Assert.NotEmpty(data.FavoritedSlots);
            Assert.NotEmpty(data.ItemsDistilled);
            Assert.Equal("food_milksac", data.ItemsDistilled[0]);
            Assert.All(data.ItemsDistilled, id => Assert.Equal(id.ToLowerInvariant(), id));
        }

        var favorites = PlayerSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561198128277890.sav"));
        Assert.Equal(35, favorites.FavoritedSlots.Count);
        Assert.Equal(9, favorites.FavoritedSlots.Count(x => x));
    }

    [SkippableFact]
    public void A_saved_effect_is_read_with_its_row_limb_and_expiry()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");

        var withBuff = PlayerSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir!, "PlayerData", BuffPlayer));
        var buff = Assert.Single(withBuff.ActiveBuffs);
        Assert.Equal("Debuff_LacticAcid_Arms", buff.BuffRow);
        Assert.Equal("EBodyLimbs::AllBones", buff.ParentLimb);
        Assert.Equal(5298.2188f, buff.ExpireTime, 0.01f);

        var without = PlayerSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir!, "PlayerData", Player));
        Assert.Empty(without.ActiveBuffs);
    }

    [SkippableFact]
    public void Last_hotbar_selection_exists_only_in_the_newest_save_and_defaults_to_null()
    {
        var chrissie = ChrissiePlayer();
        Skip.If(chrissie is null, "the Chrissie world fixture is not in this checkout");
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");

        var data = PlayerSaveReader.ReadFromFile(chrissie!);
        Assert.Equal(5, data.LastHotbarSelection);
        Assert.InRange(data.LastHotbarSelection!.Value, 0, data.Inventory.Hotbar.Count - 1);

        Assert.Null(PlayerSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir!, "PlayerData", Player)).LastHotbarSelection);
    }

    [SkippableFact]
    public void Setting_the_hotbar_selection_updates_or_creates_the_property_and_rejects_bad_slots()
    {
        var chrissie = ChrissiePlayer();
        Skip.If(chrissie is null, "the Chrissie world fixture is not in this checkout");
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");

        var existing = PlayerSaveReader.ReadFromFile(chrissie!);
        PlayerSaveWriter.ApplyLastHotbarSelection(existing, 2);
        Assert.Equal(2, Reload(existing).LastHotbarSelection);

        // An older save has no such property: it is created under the full hash-suffixed name.
        var older = PlayerSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir!, "PlayerData", Player));
        PlayerSaveWriter.ApplyLastHotbarSelection(older, 7);
        var reloaded = Reload(older);
        Assert.Equal(7, reloaded.LastHotbarSelection);
        Assert.Equal(older.Inventory.Hotbar.Count, reloaded.Inventory.Hotbar.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSaveWriter.ApplyLastHotbarSelection(older, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSaveWriter.ApplyLastHotbarSelection(older, older.Inventory.Hotbar.Count));
    }

    [SkippableFact]
    public void Transmog_flags_patch_in_place_without_resizing_and_an_untouched_save_round_trips_byte_exact()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var path = Path.Combine(Fixtures.CascadeDir!, "PlayerData", Player);
        var original = File.ReadAllBytes(path);

        var data = PlayerSaveReader.ReadFromFile(path);
        using (var ms = new MemoryStream())
        {
            data.Raw.WriteTo(ms);
            Assert.Equal(original, ms.ToArray());
        }

        var flipped = data.TransmogDisabled.Select(x => !x).Concat([true, true]).ToList();
        PlayerSaveWriter.ApplyTransmogDisabled(data, flipped);
        var after = Reload(data).TransmogDisabled;
        Assert.Equal(13, after.Count);
        Assert.Equal(flipped.Take(13), after);
    }
}
