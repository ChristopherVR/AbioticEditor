using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized overrides for <see cref="DoorClassCatalog"/> and
/// <see cref="DoorStateNames"/>. Those Core types stay English (the CLI/tests source of
/// truth, same as <c>TraderLore</c>/<c>StoryProgressionCatalog</c>); this class supplies the
/// resx-backed translations, keyed by the catalog's own stable ids, that App view-models
/// prefer instead.
/// </summary>
public static class DoorLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    // Order matches E_DoorStates::NewEnumerator0..6 (see DoorStateNames).
    private static readonly string[] StateKeys =
    {
        "WorldDoors_State_Closed",
        "WorldDoors_State_Open",
        "WorldDoors_State_Locked",
        "WorldDoors_State_Opening",
        "WorldDoors_State_Closing",
        "WorldDoors_State_Jammed",
        "WorldDoors_State_Broken",
    };

    /// <summary>Localized door-class display name. Falls back to the Core English name for a
    /// class not in the catalog (an unknown/future blueprint has no translation to look up).</summary>
    public static string ClassDisplayName(DoorClassInfo info)
        => DoorClassCatalog.KnownClasses.ContainsKey(info.ClassName)
            ? Loc[$"WorldDoors_Class_{info.ClassName}_DisplayName"]
            : info.DisplayName;

    /// <summary>Localized reference prose for a lock kind (see <see cref="DoorClassCatalog.LockExplanation"/>).</summary>
    public static string LockExplanation(string lockKind) => Loc[$"WorldDoors_LockExplanation_{lockKind}"];

    /// <summary>Localized friendly door-state label. Unknown/future enumerators keep the Core
    /// fallback (a diagnostic "State N" or the raw value), since there is nothing to translate.</summary>
    public static string FriendlyState(string? rawEnumValue)
    {
        var idx = DoorStateNames.TryParseIndex(rawEnumValue);
        if (idx is { } i && i >= 0 && i < StateKeys.Length)
        {
            return Loc[StateKeys[i]];
        }
        return DoorStateNames.Friendly(rawEnumValue);
    }

    /// <summary>Localized labels for all known door states, in enum order.</summary>
    public static IReadOnlyList<string> AllFriendlyStateNames
        => Array.ConvertAll(StateKeys, k => Loc[k]);
}
