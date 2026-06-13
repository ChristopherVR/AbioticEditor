using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;

namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// Builds <see cref="CompatibilityReport"/>s for opened saves. The version comparison
/// comes from <see cref="SaveVersionRegistry"/>; unknown-content counts come from the
/// flag/chapter catalogs and (optionally) an <see cref="UnknownContentCollector"/> that
/// was live while the save was read.
/// </summary>
public static class CompatibilityAnalyzer
{
    private static readonly HashSet<string> KnownFlagSet =
        new(QuestFlagCatalog.KnownFlags, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a world save and returns it together with its compatibility report. The
    /// read runs inside a collector scope, so unmodeled keys / unknown enum values are
    /// counted even when <see cref="Diagnostics.EditorLog"/> is disabled.
    /// </summary>
    public static (WorldSaveData Data, CompatibilityReport Report) ReadWorldWithReport(string path)
    {
        using var collector = UnknownContentCollector.Begin();
        var data = WorldSaveReader.ReadFromFile(path);
        return (data, AnalyzeWorld(data, collector));
    }

    /// <summary>
    /// Reads a player save and returns it together with its compatibility report; see
    /// <see cref="ReadWorldWithReport"/>.
    /// </summary>
    public static (PlayerSaveData Data, CompatibilityReport Report) ReadPlayerWithReport(string path)
    {
        using var collector = UnknownContentCollector.Begin();
        var data = PlayerSaveReader.ReadFromFile(path);
        return (data, AnalyzePlayer(data, collector));
    }

    /// <summary>Reports on an already-loaded world (or metadata) save.</summary>
    public static CompatibilityReport AnalyzeWorld(WorldSaveData data, UnknownContentCollector? collected = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Analyze(data.Raw, data.Flags, data.StoryProgressionRow, collected);
    }

    /// <summary>Reports on an already-loaded player save.</summary>
    public static CompatibilityReport AnalyzePlayer(PlayerSaveData data, UnknownContentCollector? collected = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Analyze(data.Raw, worldFlags: null, storyProgressionRow: null, collected);
    }

    /// <summary>
    /// Core report builder for any loaded <see cref="SaveGame"/>.
    /// </summary>
    /// <param name="save">The loaded save tree.</param>
    /// <param name="worldFlags">World flags from the save, when it has them - checked against <see cref="QuestFlagCatalog.KnownFlags"/>.</param>
    /// <param name="storyProgressionRow">The metadata save's current chapter row, when present - checked against <see cref="StoryProgressionCatalog"/>.</param>
    /// <param name="collected">Unknown-content observations captured while the save was read, when available.</param>
    public static CompatibilityReport Analyze(
        SaveGame save,
        IEnumerable<string>? worldFlags = null,
        string? storyProgressionRow = null,
        UnknownContentCollector? collected = null)
    {
        ArgumentNullException.ThrowIfNull(save);

        var kind = SaveVersionRegistry.KindOf(save);
        var version = SaveVersionRegistry.GetAbfVersion(save);
        var known = SaveVersionRegistry.Find(kind);

        string[] unknownFlags = worldFlags is null
            ? Array.Empty<string>()
            : worldFlags.Where(f => !KnownFlagSet.Contains(f)).ToArray();

        var unknownChapter = storyProgressionRow is not null && StoryProgressionCatalog.Find(storyProgressionRow) is null
            ? storyProgressionRow
            : null;

        var unknownKeys = collected?.UnknownPropertyKeys ?? Array.Empty<string>();
        var unknownEnums = collected?.UnknownEnumValues ?? Array.Empty<string>();

        var hasUnknownContent = unknownFlags.Length > 0 || unknownChapter is not null
                                || unknownKeys.Count > 0 || unknownEnums.Count > 0;

        return new CompatibilityReport
        {
            Kind = kind,
            SaveClassName = save.SaveClass?.Value,
            VersionSeen = version,
            Known = known,
            Severity = SaveVersionRegistry.Classify(kind, version, hasUnknownContent),
            UnknownFlags = unknownFlags,
            UnknownPropertyKeys = unknownKeys,
            UnknownEnumValues = unknownEnums,
            UnknownStoryChapter = unknownChapter,
        };
    }
}
