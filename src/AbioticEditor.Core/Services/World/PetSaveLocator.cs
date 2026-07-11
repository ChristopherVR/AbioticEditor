namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Resolves sibling saves for cross-save pet moves, following the game's folder layout: a
/// world save (<c>Worlds/&lt;name&gt;/WorldSave_*.sav</c>) sits next to a <c>PlayerData/</c>
/// directory of <c>Player_*.sav</c> files.
/// </summary>
public static class PetSaveLocator
{
    /// <summary>The <c>Player_*.sav</c> files for the world that owns <paramref name="worldSavePath"/>.</summary>
    public static IReadOnlyList<string> SiblingPlayerSaves(string worldSavePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(worldSavePath));
        if (dir is null) return Array.Empty<string>();

        foreach (var candidate in new[] { Path.Combine(dir, "PlayerData"), dir, Path.Combine(dir, "..", "PlayerData") })
        {
            if (Directory.Exists(candidate))
            {
                var players = Directory.EnumerateFiles(candidate, "Player_*.sav", SearchOption.TopDirectoryOnly).ToList();
                if (players.Count > 0) return players;
            }
        }
        return Array.Empty<string>();
    }

    /// <summary>The <c>WorldSave_*.sav</c> files for the world that owns <paramref name="playerSavePath"/>.</summary>
    public static IReadOnlyList<string> SiblingWorldSaves(string playerSavePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(playerSavePath));
        if (dir is null) return Array.Empty<string>();

        // Player_*.sav usually lives under PlayerData/; world saves are a level up. Also try the same dir.
        foreach (var candidate in new[] { Path.GetDirectoryName(dir), dir })
        {
            if (candidate is not null && Directory.Exists(candidate))
            {
                var worlds = Directory.EnumerateFiles(candidate, "WorldSave_*.sav", SearchOption.TopDirectoryOnly)
                    .Where(w => !Path.GetFileName(w).Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (worlds.Count > 0) return worlds;
            }
        }
        return Array.Empty<string>();
    }
}
