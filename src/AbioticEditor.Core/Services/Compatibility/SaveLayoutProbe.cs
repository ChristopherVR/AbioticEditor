using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;

namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// Looks at a loaded save and counts the top-level properties the readers have no model for.
/// A handful is normal (the game adds small fields all the time and they are preserved on
/// save). A large number means the game renamed or restructured the save, which no version
/// number is guaranteed to announce: the readers then find nothing and a write would add fields
/// the game ignores. Works on the tree alone, so it needs no read-time observer.
/// </summary>
public static class SaveLayoutProbe
{
    /// <summary>
    /// At or above this many unmodeled top-level properties a save is treated as a different
    /// layout. Every legacy fixture carries at most one; the first game-written save from the
    /// restructured layout carried eighteen or more.
    /// </summary>
    public const int LayoutChangeThreshold = 5;

    /// <summary>The unmodeled top-level property names of <paramref name="save"/>, hash suffixes removed.</summary>
    public static IReadOnlyList<string> UnmodeledKeys(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var kind = SaveVersionRegistry.KindOf(save);
        IEnumerable<FPropertyTag>? tags;
        Func<string, bool> isModeled;
        switch (kind)
        {
            case SaveKind.Character:
                var members = PlayerSaveReader.GetCharacterSaveData(save);
                tags = members;
                isModeled = name => PlayerSaveReader.IsModeledKey(name, members);
                break;
            case SaveKind.World:
            case SaveKind.Metadata:
                tags = save.Properties;
                isModeled = WorldSaveReader.IsModeledTopLevelKey;
                break;
            default:
                return Array.Empty<string>();
        }

        if (tags is null) return Array.Empty<string>();
        var area = kind == SaveKind.Character ? "PlayerSave" : "WorldSave";
        return tags
            .Select(tag => tag.Name?.Value)
            .Where(name => name is not null && !isModeled(name))
            .Select(name => $"{area}: {UnmodeledFieldCensus.Normalize(name!)}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True when <paramref name="unmodeledKeyCount"/> is high enough to indicate a changed layout.</summary>
    public static bool IndicatesChangedLayout(int unmodeledKeyCount) => unmodeledKeyCount >= LayoutChangeThreshold;
}
