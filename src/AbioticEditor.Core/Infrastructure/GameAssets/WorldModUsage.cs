namespace AbioticEditor.Core.Assets;

/// <summary>Where one of a world's mods stands in the editor.</summary>
public enum WorldModState
{
    /// <summary>Installed and loaded: its items, recipes and pictures are known.</summary>
    Loaded,

    /// <summary>Installed on this PC but switched off in the editor.</summary>
    Off,

    /// <summary>Subscribed, but the game has not finished downloading it.</summary>
    Downloading,

    /// <summary>Not on this PC. Playing the world once makes the game download it.</summary>
    Missing,
}

/// <summary>One mod the world uses, or one the editor loads that the world does not use.</summary>
public sealed record WorldModEntry(long ModIoId, string DisplayName, WorldModState State);

/// <summary>
/// Compares the mods a world last ran with (<see cref="WorldSaves.WorldSaveData.LastUsedModIds"/>)
/// against what is installed and loaded, so the editor can say when a world's modded things will
/// show as unknown, and which loaded mods the world never used.
/// </summary>
public sealed record WorldModUsage(IReadOnlyList<WorldModEntry> WorldMods, IReadOnlyList<WorldModEntry> ExtraLoaded)
{
    /// <summary>True when every mod the world uses is loaded and nothing else is.</summary>
    public bool Matches => WorldMods.All(mod => mod.State == WorldModState.Loaded) && ExtraLoaded.Count == 0;

    /// <summary>The world's mods the editor cannot read right now.</summary>
    public IEnumerable<WorldModEntry> Unavailable => WorldMods.Where(mod => mod.State != WorldModState.Loaded);

    /// <summary>Builds the comparison from this PC's mod.io installs and the editor's loaded mods.</summary>
    /// <param name="loadedMods">The provider's <see cref="GameAssetProvider.LoadedMods"/> (mod keys).</param>
    public static WorldModUsage For(IReadOnlyList<long> worldModIds, IReadOnlyCollection<string> loadedMods)
        => For(worldModIds, loadedMods, ModIoInstallLocator.FindInstalled(), ModIoInstallLocator.FindPending());

    public static WorldModUsage For(
        IReadOnlyList<long> worldModIds,
        IReadOnlyCollection<string> loadedMods,
        IReadOnlyList<AfInstallLocator.InstalledMod> installed,
        IReadOnlyList<ModIoInstallLocator.PendingMod> pending)
    {
        var loaded = new HashSet<string>(loadedMods, StringComparer.OrdinalIgnoreCase);
        var byId = installed.Where(mod => mod.ModIoId is not null).GroupBy(mod => mod.ModIoId!.Value).ToDictionary(g => g.Key, g => g.First());
        var waiting = pending.GroupBy(mod => mod.ModIoId).ToDictionary(g => g.Key, g => g.First());
        var world = worldModIds.Distinct().Select(id =>
        {
            if (byId.TryGetValue(id, out var mod))
                return new WorldModEntry(id, mod.DisplayName, loaded.Contains(mod.Name) ? WorldModState.Loaded : WorldModState.Off);
            return waiting.TryGetValue(id, out var download)
                ? new WorldModEntry(id, download.DisplayName, WorldModState.Downloading)
                : new WorldModEntry(id, $"mod.io #{id}", WorldModState.Missing);
        }).ToArray();
        var used = worldModIds.ToHashSet();
        var extra = byId.Values.Where(mod => loaded.Contains(mod.Name) && !used.Contains(mod.ModIoId!.Value))
            .Select(mod => new WorldModEntry(mod.ModIoId!.Value, mod.DisplayName, WorldModState.Loaded))
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(world, extra);
    }
}
