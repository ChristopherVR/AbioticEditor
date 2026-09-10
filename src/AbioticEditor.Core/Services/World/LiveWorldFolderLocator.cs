using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Finds which world folder on this machine a live-connected player's save lives in, so the live
/// sidebar can show that world's other region saves (disabled, via <see cref="WorldAreaCatalog"/>)
/// and its offline players alongside the connected ones. A live connection only ever hands back a
/// player id (the game's own <c>UniquePlayerID</c>, a SteamID64 on Steam - see
/// <c>LivePlayerDirectoryChannel</c>), never a world/folder name, so the match runs the other way:
/// which discovered world already has a <c>PlayerData\Player_&lt;id&gt;.sav</c> for that id. Ties
/// (the same account has played more than one world) are broken by most-recently-played, the same
/// signal the world picker itself sorts by.
/// </summary>
public static class LiveWorldFolderLocator
{
    /// <summary>
    /// The most recently played discovered world whose <c>PlayerData</c> folder has a save for
    /// <paramref name="playerId"/>, or null when none was found (a fresh character with no save
    /// yet, a world this machine cannot see, or an empty/unknown id).
    /// </summary>
    /// <param name="worlds">Override for tests; defaults to a fresh <see
    /// cref="SaveDiscovery.DiscoverAll"/> scan of this machine.</param>
    public static DiscoveredWorld? FindForPlayer(string? playerId, IEnumerable<DiscoveredWorld>? worlds = null)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        var candidates = worlds ?? SaveDiscovery.DiscoverAll();
        return candidates
            .Where(world => !world.IsGamePassContainer
                && File.Exists(Path.Combine(world.FolderPath, "PlayerData", $"Player_{playerId}.sav")))
            .OrderByDescending(world => world.LastPlayed)
            .FirstOrDefault();
    }
}
