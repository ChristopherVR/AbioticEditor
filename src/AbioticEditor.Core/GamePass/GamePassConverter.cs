using UeSaveGame;
using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// Converts a save between the two on-disk shapes: a Steam (loose-file) world folder
/// (<c>WorldSave_*.sav</c> + <c>PlayerData/Player_*.sav</c>) and a Game Pass / Xbox "wgs" container
/// (an <c>ABF_SAVE_VERSION</c> bundle of the same members in one Oodle-compressed blob). The save
/// content is identical either way - only the packaging differs - so the conversion is lossless for
/// every member it carries.
/// </summary>
public static class GamePassConverter
{
    static GamePassConverter()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Packs the Steam world folder at <paramref name="steamWorldDir"/> into a new Game Pass wgs
    /// container at <paramref name="destWgsDir"/>. Returns the wgs folder path. The player/world
    /// saves keep their ids; on Game Pass the local Xbox account loads the world, so a player may
    /// need re-homing to the target account id (see <see cref="PlayerSaves.PlayerSaveIdentity"/>).
    /// </summary>
    public static string SteamWorldToGamePass(string steamWorldDir, string destWgsDir, string? worldName = null)
    {
        if (!Directory.Exists(steamWorldDir))
        {
            throw new DirectoryNotFoundException($"Steam world folder not found: {steamWorldDir}");
        }
        worldName ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(steamWorldDir)));

        var members = new List<AbfMember>();
        foreach (var file in EnumerateWorldSaves(steamWorldDir))
        {
            var bytes = File.ReadAllBytes(file.Path);
            var saveClass = ReadSaveClass(bytes);
            if (saveClass is null || !GamePassMemberCodec.IsEditableSaveClass(saveClass))
            {
                Diagnostics.EditorLog.Info("GamePass",
                    $"Skipping '{Path.GetFileName(file.Path)}' (unsupported save class '{saveClass ?? "?"}').");
                continue;
            }
            var body = GamePassMemberCodec.ToMemberBody(saveClass, bytes);
            // In-bundle member paths use forward slashes, no extension, under Profile/Worlds/<World>.
            var rel = file.Relative.Replace('\\', '/');
            if (rel.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) rel = rel[..^4];
            members.Add(new AbfMember
            {
                Path = $"Profile/Worlds/{worldName}/{rel}",
                SaveClass = saveClass,
                Flag = 0,
                Body = body,
            });
        }

        if (members.Count == 0)
        {
            throw new InvalidDataException($"No Abiotic Factor saves found under '{steamWorldDir}'.");
        }

        var blob = AbfSaveBundle.Create(members).Serialize();
        WgsContainerStore.WriteNewContainer(destWgsDir, $"{worldName}-WC", blob);
        Diagnostics.EditorLog.Info("GamePass",
            $"Converted Steam world '{worldName}' ({members.Count} save(s)) -> Game Pass container at {destWgsDir}.");
        return destWgsDir;
    }

    /// <summary>
    /// Unpacks a Game Pass world container into a Steam world folder at
    /// <paramref name="destSteamDir"/> (loose <c>.sav</c> files). When <paramref name="containerName"/>
    /// is null the single world container is used (or the only one). Returns the world folder path.
    /// </summary>
    public static string GamePassToSteamWorld(string wgsDir, string? containerName, string destSteamDir)
    {
        var set = GamePassSaveSet.Open(wgsDir);
        var container = containerName
            ?? set.Entries().Select(e => e.ContainerName).Distinct().FirstOrDefault()
            ?? throw new InvalidDataException($"No world containers found in '{wgsDir}'.");
        set.ExtractWorld(container, destSteamDir);
        Diagnostics.EditorLog.Info("GamePass",
            $"Converted Game Pass container '{container}' -> Steam world folder at {destSteamDir}.");
        return destSteamDir;
    }

    private static IEnumerable<(string Path, string Relative)> EnumerateWorldSaves(string worldDir)
    {
        foreach (var f in Directory.EnumerateFiles(worldDir, "WorldSave_*.sav", SearchOption.TopDirectoryOnly))
        {
            yield return (f, Path.GetFileName(f));
        }
        var playerData = Path.Combine(worldDir, "PlayerData");
        if (Directory.Exists(playerData))
        {
            foreach (var f in Directory.EnumerateFiles(playerData, "Player_*.sav", SearchOption.TopDirectoryOnly))
            {
                yield return (f, Path.Combine("PlayerData", Path.GetFileName(f)));
            }
        }
    }

    private static string? ReadSaveClass(byte[] gvas)
    {
        try
        {
            using var ms = new MemoryStream(gvas, writable: false);
            return SaveGame.LoadFrom(ms).SaveClass?.Value;
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("GamePass", $"Could not read save class: {ex.Message}");
            return null;
        }
    }
}
