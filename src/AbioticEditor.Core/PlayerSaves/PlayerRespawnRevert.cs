namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Moves every player's respawn/load-in point back to a story chapter's punch-card terminal -
/// the position counterpart of a story rewind. <see cref="WorldSaves.StoryFlagSync.ClearForwardFlags"/>
/// resets quest state but never touches where a player loads back in, which could otherwise
/// leave them standing somewhere the reverted story hasn't reached yet. Only X/Y/Z and the
/// terminal id are touched - the same fields changed by picking a terminal (without also
/// picking a different streamed sub-level) in the PLAYER &gt; SPAWN tab; <c>LastSafeWorldGUID_</c>
/// is left as-is, since that tab already treats the two as independently editable.
/// </summary>
public static class PlayerRespawnRevert
{
    /// <summary>
    /// Writes <paramref name="chapterRow"/>'s terminal position to every <c>Player_*.sav</c>
    /// in the <c>PlayerData</c> folder next to <paramref name="metadataSavePath"/> (standard
    /// pre-write backup each). A no-op (with an explanatory message) when the chapter has no
    /// known terminal or no player saves are found.
    /// </summary>
    public static (int PlayersMoved, string Message) MoveToChapterTerminal(string metadataSavePath, string chapterRow)
    {
        var terminal = RespawnTerminalCatalog.ForChapter(chapterRow);
        if (terminal is null)
        {
            return (0, $"No known respawn terminal for chapter '{chapterRow}' or anything earlier in the story.");
        }

        var folder = Path.GetDirectoryName(metadataSavePath);
        var playerDir = folder is null ? null : Path.Combine(folder, "PlayerData");
        if (playerDir is null || !Directory.Exists(playerDir))
        {
            return (0, "No PlayerData folder found next to the metadata save.");
        }

        var moved = 0;
        foreach (var playerPath in Directory.EnumerateFiles(playerDir, "Player_*.sav"))
        {
            var data = PlayerSaveReader.ReadFromFile(playerPath);
            PlayerSaveWriter.ApplyRespawn(data, terminal.X, terminal.Y, terminal.Z);
            PlayerSaveWriter.ApplyRespawnTerminal(data, terminal.TerminalGuid);
            PlayerSaveWriter.WriteToFile(data, playerPath);
            moved++;
        }

        return (moved, moved == 0
            ? "No player saves found to move."
            : $"Moved {moved} player(s) to the {terminal.LocationName} terminal (backups kept).");
    }
}
