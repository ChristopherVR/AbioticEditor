using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Enumerates every world folder among the fixtures (legacy Steam, current Steam client and
/// dedicated server) and loads their saves once, so the research/census tests can sweep the whole
/// fixture set without each re-parsing the large region files. Everything is empty when the
/// fixtures are absent, so callers skip gracefully.
/// </summary>
internal static class AllFixtureSaves
{
    private static readonly Lazy<IReadOnlyList<string>> WorldDirsLazy = new(FindWorldDirs);
    // Held weakly: every fixture world parsed is roughly 190 MB of save files and several GB once
    // parsed, and a plain static kept all of it alive for the rest of the run after the first
    // census test touched it. Tests that are using the list keep it alive; once none is, the
    // memory can be reclaimed and the next caller parses again.
    private static readonly WeakReference<IReadOnlyList<(string Path, WorldSaveData Data)>?> WorldCache = new(null);
    private static readonly Lock WorldCacheLock = new();

    private static IReadOnlyList<(string Path, WorldSaveData Data)> LoadWorldSaves()
    {
        lock (WorldCacheLock)
        {
            if (WorldCache.TryGetTarget(out var cached) && cached is not null) return cached;
            var loaded = WorldDirs.SelectMany(d => Directory.EnumerateFiles(d, "WorldSave_*.sav").Order(StringComparer.Ordinal))
                .Select(p => (p, WorldSaveReader.ReadFromFile(p))).ToList();
            WorldCache.SetTarget(loaded);
            return loaded;
        }
    }

    /// <summary>World folders that hold <c>WorldSave_*.sav</c> files, in a fixed order.</summary>
    public static IReadOnlyList<string> WorldDirs => WorldDirsLazy.Value;

    /// <summary>Every parsed <c>WorldSave_*.sav</c> (regions and metadata) across all world folders.</summary>
    public static IReadOnlyList<(string Path, WorldSaveData Data)> WorldSaves => LoadWorldSaves();

    /// <summary>Just the region saves (everything except <c>WorldSave_MetaData.sav</c>).</summary>
    public static IEnumerable<(string Path, WorldSaveData Data)> RegionSaves
        => WorldSaves.Where(w => !IsMetadata(w.Path));

    /// <summary>Just the metadata saves.</summary>
    public static IEnumerable<(string Path, WorldSaveData Data)> MetadataSaves
        => WorldSaves.Where(w => IsMetadata(w.Path));

    /// <summary>Every <c>Player_*.sav</c> under any fixture world folder, parsed.</summary>
    public static IEnumerable<(string Path, PlayerSaveData Data)> PlayerSaves()
        => WorldDirs.Select(d => Path.Combine(d, "PlayerData")).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "Player_*.sav").Order(StringComparer.Ordinal))
            .Select(p => (p, PlayerSaveReader.ReadFromFile(p)));

    private static bool IsMetadata(string path)
        => string.Equals(System.IO.Path.GetFileName(path), "WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase);

    private static List<string> FindWorldDirs()
    {
        var dirs = new List<string>();
        void Add(string? d)
        {
            if (d is not null && Directory.Exists(d) && !dirs.Contains(d)) dirs.Add(d);
        }
        Add(Fixtures.CascadeDir);
        Add(Fixtures.ServerWorldsDir);
        foreach (var meta in Fixtures.ClientWorldSaves("WorldSave_MetaData.sav"))
        {
            Add(System.IO.Path.GetDirectoryName(meta));
        }
        return dirs;
    }
}
