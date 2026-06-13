using UeSaveGame;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Builds new player saves: a reset-to-blank routine, a bundled-template builder and the
/// create-from-template path the editor's "add player" flow uses.
///
/// Abiotic Factor has no documented "make a fresh character save" API we can call, and the
/// file is a delta-serialized blueprint tree, so a brand-new player is fabricated from an
/// existing save's <i>structure</i> with every progression field reset. The reset reuses
/// <see cref="PlayerSaveWriter"/> so it stays in lock-step with how the editor writes saves.
/// </summary>
public static class PlayerSaveFactory
{
    static PlayerSaveFactory()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Resets <paramref name="data"/> in place to a fresh-character baseline: full vitals
    /// and limb health, zero money, all skills back to 0 XP, every unlock/compendium/kill
    /// list emptied and every inventory slot cleared. The save's structural shape (array
    /// lengths, skill positions) is preserved - only values change. Does not write to disk.
    /// </summary>
    public static void ResetToBlank(PlayerSaveData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Vitals full, wallet empty.
        PlayerSaveWriter.ApplyStats(data, new CharacterStats(100, 100, 100, 100, 100, Money: 0));
        PlayerSaveWriter.ApplyLimbHealth(data, LimbHealth.Full);

        // Skills back to level 0 (0 XP), multiplier neutral - keep the positional array.
        PlayerSaveWriter.ApplySkills(
            data, data.Skills.Select(s => new PlayerSkill(s.Index, Xp: 0, XpMultiplier: 1)).ToList());

        // Wipe every progression list.
        PlayerSaveWriter.ApplyTraits(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyRecipes(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyEmailsRead(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyJournals(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyCompendium(data, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        PlayerSaveWriter.ApplyItemsPickedUp(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyCraftedItems(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyMapsUnlocked(data, Array.Empty<string>());
        PlayerSaveWriter.ApplyFishCaught(data, Array.Empty<string>());

        // Kill tallies: the array only grows in-game, so we can't remove entries - zero
        // the ones that exist instead.
        PlayerSaveWriter.ApplyKillCounts(
            data, data.KillCounts.Select(k => new KillCount(k.CompendiumRow, Count: 0)).ToList());

        // Empty the bags.
        PlayerSaveWriter.ClearAllInventory(data);
    }

    /// <summary>
    /// Reads the player save at <paramref name="sourcePath"/>, resets it to a blank
    /// character and returns the serialized bytes with a neutral (empty) owner identifier.
    /// Used once to generate the bundled <c>blank-player-template.sav</c> asset.
    /// </summary>
    public static byte[] BuildBlankTemplate(string sourcePath)
    {
        var data = PlayerSaveReader.ReadFromFile(sourcePath);
        ResetToBlank(data);
        PlayerSaveIdentity.StampIdentifier(data.Raw, string.Empty);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a new <c>Player_&lt;steamId&gt;.sav</c> in <paramref name="destDir"/> from
    /// the blank-template <paramref name="templateBytes"/>, stamping the new owner's
    /// <c>SaveIdentifier</c>. Returns the new file's path.
    /// </summary>
    /// <exception cref="IOException">A save for <paramref name="steamId"/> already exists.</exception>
    public static string CreateFromTemplate(byte[] templateBytes, string destDir, ulong steamId)
    {
        ArgumentNullException.ThrowIfNull(templateBytes);
        using var ms = new MemoryStream(templateBytes, writable: false);
        var save = SaveGame.LoadFrom(ms);
        Diagnostics.EditorLog.Info("PlayerSave",
            $"Creating new blank Player_{steamId}.sav from bundled template");
        return PlayerSaveIdentity.WriteAs(save, destDir, steamId);
    }
}
