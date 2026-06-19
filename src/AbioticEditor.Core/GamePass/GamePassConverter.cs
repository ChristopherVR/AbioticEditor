using UeSaveGame;
using AbioticEditor.Core.PlayerSaves;
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
    /// container at <paramref name="destWgsDir"/>. Returns the wgs folder path.
    /// <para>When <paramref name="newPlayerId"/> is set the (single) player save is re-homed to that
    /// account id - the id in its file name and its <c>SaveIdentifier</c> - so it belongs to the
    /// target account. Leave it null to keep the existing ids. Re-homing requires a single-player
    /// world (one id can't own several characters).</para>
    /// </summary>
    public static string SteamWorldToGamePass(
        string steamWorldDir, string destWgsDir, string? worldName = null, string? newPlayerId = null)
    {
        if (!Directory.Exists(steamWorldDir))
        {
            throw new DirectoryNotFoundException($"Steam world folder not found: {steamWorldDir}");
        }
        worldName ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(steamWorldDir)));
        newPlayerId = ValidateOptionalId(newPlayerId);

        var saves = EnumerateWorldSaves(steamWorldDir).ToList();
        GuardSingleRehome(newPlayerId, saves.Count(s => IsPlayerSave(s.Relative)));

        var members = new List<AbfMember>();
        foreach (var file in saves)
        {
            var bytes = File.ReadAllBytes(file.Path);
            var saveClass = ReadSaveClass(bytes);
            if (saveClass is null || !GamePassMemberCodec.IsEditableSaveClass(saveClass))
            {
                Diagnostics.EditorLog.Info("GamePass",
                    $"Skipping '{Path.GetFileName(file.Path)}' (unsupported save class '{saveClass ?? "?"}').");
                continue;
            }

            // In-bundle member paths use forward slashes, no extension, under Profile/Worlds/<World>.
            var rel = file.Relative.Replace('\\', '/');
            if (rel.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) rel = rel[..^4];

            if (newPlayerId is not null && saveClass == GamePassMemberCodec.CharacterSaveClass)
            {
                bytes = StampOwner(bytes, newPlayerId);
                rel = $"PlayerData/Player_{newPlayerId}";
            }

            members.Add(new AbfMember
            {
                Path = $"Profile/Worlds/{worldName}/{rel}",
                SaveClass = saveClass,
                Flag = 0,
                Body = GamePassMemberCodec.ToMemberBody(saveClass, bytes),
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
    /// is null the only world container is used. When <paramref name="newPlayerId"/> is set the
    /// (single) player save is re-homed to that account id so the world belongs to the target Steam
    /// account; leave it null to keep the existing ids. Returns the world folder path.
    /// </summary>
    public static string GamePassToSteamWorld(
        string wgsDir, string? containerName, string destSteamDir, string? newPlayerId = null)
    {
        newPlayerId = ValidateOptionalId(newPlayerId);
        var set = GamePassSaveSet.Open(wgsDir);
        var container = containerName
            ?? set.Entries().Select(e => e.ContainerName).Distinct().FirstOrDefault()
            ?? throw new InvalidDataException($"No world containers found in '{wgsDir}'.");
        set.ExtractWorld(container, destSteamDir);

        if (newPlayerId is not null)
        {
            var playerDir = Path.Combine(destSteamDir, "PlayerData");
            var players = Directory.Exists(playerDir)
                ? Directory.GetFiles(playerDir, "Player_*.sav")
                : Array.Empty<string>();
            GuardSingleRehome(newPlayerId, players.Length);
            if (players.Length == 1)
            {
                PlayerSaveIdentity.ChangeSteamId(players[0], newPlayerId);
                var bak = players[0] + ".bak";
                if (File.Exists(bak)) File.Delete(bak); // the freshly-extracted original needs no backup
            }
        }

        Diagnostics.EditorLog.Info("GamePass",
            $"Converted Game Pass container '{container}' -> Steam world folder at {destSteamDir}.");
        return destSteamDir;
    }

    private static bool IsPlayerSave(string relative)
        => Path.GetFileName(relative).StartsWith("Player_", StringComparison.OrdinalIgnoreCase);

    private static string? ValidateOptionalId(string? id)
    {
        id = id?.Trim();
        if (string.IsNullOrEmpty(id)) return null;
        if (!PlayerIdentifier.IsSafeFileToken(id))
        {
            throw new ArgumentException(
                $"'{id}' is not a valid account id (use letters, digits, '-', '_' or '.').", nameof(id));
        }
        return id;
    }

    private static void GuardSingleRehome(string? newPlayerId, int playerCount)
    {
        if (newPlayerId is not null && playerCount > 1)
        {
            throw new InvalidOperationException(
                "Re-homing to a single account id needs a single-player world; this world has "
                + $"{playerCount} player saves. Convert without an id to keep the existing ones.");
        }
    }

    private static byte[] StampOwner(byte[] gvas, string newId)
    {
        using var inMs = new MemoryStream(gvas, writable: false);
        var save = SaveGame.LoadFrom(inMs);
        PlayerSaveIdentity.StampIdentifier(save, newId);
        using var outMs = new MemoryStream();
        save.WriteTo(outMs);
        return outMs.ToArray();
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
