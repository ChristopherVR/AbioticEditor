using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized override for <see cref="NpcIdentityCatalog"/>. The Core catalog stays
/// English (the CLI source of truth); this maps its matched hint id to a resx-backed
/// translation, mirroring the DoorLocalization pattern.
/// </summary>
public static class NpcLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    public static string LabelFor(string id, string actorName)
        => NpcIdentityCatalog.MatchedHint(id, actorName) is { } hint
            ? Loc[$"WorldNpc_Identity_{hint.Trim('_')}"]
            : Loc["WorldNpc_Identity_Default"];
}
