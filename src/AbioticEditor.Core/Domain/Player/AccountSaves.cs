namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One entry of <c>Unlocks.sav</c> (<c>CustomizationUnlocks</c>, an array of DataTable row names).
/// </summary>
/// <param name="RowName">The row name exactly as stored, for example <c>Head_M01chemist</c>.</param>
/// <param name="Category">Best-effort appearance slot inferred from the row-name prefix, or "Other".</param>
public sealed record CustomizationUnlock(string RowName, string Category);

/// <summary>Read-only model of <c>Unlocks.sav</c> (save class <c>Abiotic_CustomizationUnlocks_Save_C</c>).</summary>
public sealed record CustomizationUnlocksModel(IReadOnlyList<CustomizationUnlock> Unlocks)
{
    /// <summary>Unlocked row names, in file order.</summary>
    public IEnumerable<string> RowNames => Unlocks.Select(u => u.RowName);

    /// <summary>
    /// Splits a catalog table's rows into owned and not-yet-owned rows. Only rows present in
    /// <paramref name="catalogRows"/> are ever reported as unavailable, so an invalid unlock
    /// name is never offered.
    /// </summary>
    public (IReadOnlyList<string> Owned, IReadOnlyList<string> Unavailable) Partition(IEnumerable<string> catalogRows)
    {
        var owned = new HashSet<string>(RowNames, StringComparer.OrdinalIgnoreCase);
        var ownedRows = new List<string>();
        var missing = new List<string>();
        foreach (var row in catalogRows)
        {
            (owned.Contains(row) ? ownedRows : missing).Add(row);
        }
        return (ownedRows, missing);
    }

    /// <summary>Unlock names not found in <paramref name="catalogRows"/> (stale or renamed rows).</summary>
    public IReadOnlyList<string> UnknownTo(IEnumerable<string> catalogRows)
    {
        var known = new HashSet<string>(catalogRows, StringComparer.OrdinalIgnoreCase);
        return RowNames.Where(r => !known.Contains(r)).ToList();
    }
}

/// <summary>Read-only model of <c>PlayerStatsSave.sav</c> (native class <c>/Script/AbioticFactor.PlayerStatsSave</c>).</summary>
/// <param name="Stats"><c>Stats_Int</c>: stat name (for example <c>STAT_KILLS_PEST</c>) to counter.</param>
/// <param name="Achievements"><c>Achievements</c>: local mirror of unlocked achievement API names (<c>ACH_*</c>).</param>
public sealed record PlayerStatsModel(
    IReadOnlyDictionary<string, int> Stats,
    IReadOnlyList<string> Achievements)
{
    /// <summary>Stats whose name starts with <c>STAT_KILLS_</c>.</summary>
    public IEnumerable<KeyValuePair<string, int>> KillCounters
        => Stats.Where(kv => kv.Key.StartsWith("STAT_KILLS_", StringComparison.Ordinal));
}

/// <summary>
/// Host preferences stored in <c>UserSettings.sav</c>. Password-like values are never carried:
/// only whether one is set.
/// </summary>
/// <param name="Flags">Boolean leaves (for example the single-player toggle), keyed by trimmed name.</param>
/// <param name="HasPassword">True when a non-empty password-like string is stored. The value is deliberately not modeled.</param>
public sealed record HostPreferencesModel(
    IReadOnlyDictionary<string, bool> Flags,
    bool HasPassword);

/// <summary>Read-only model of <c>UserSettings.sav</c> (save class <c>Abiotic_SettingsSave_C</c>).</summary>
public sealed record UserSettingsModel(
    IReadOnlyList<string> FavouriteRecipes,
    IReadOnlyList<string> PinnedRecipes,
    bool? HasCreatedACharacter,
    bool? HasPlayedTutorial,
    IReadOnlyList<string> UiPopupsSeen,
    IReadOnlyList<string> TutorialHintPopupsSeen,
    IReadOnlyList<string> TutorialPanelsSeen,
    HostPreferencesModel? HostPreferences,
    IReadOnlyList<string> RecentServers,
    IReadOnlyList<string> UnmodeledProperties);
