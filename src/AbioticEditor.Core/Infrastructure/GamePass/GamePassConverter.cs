using UeSaveGame;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.WorldSaves;

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
    /// <para>When <paramref name="newPlayerId"/> is set a character is re-homed to that account id -
    /// the id in its file name and its <c>SaveIdentifier</c>, plus the beds it claimed - so it
    /// belongs to the target account. Leave it null to keep the existing ids. On a world with several
    /// characters say which one with <paramref name="sourcePlayerId"/>; the others are packed
    /// unchanged.</para>
    /// <para>To re-home more than one character in the same run (a co-op world where several
    /// people are converting together), pass <paramref name="rehomes"/> instead - a map of each
    /// character's current account id to the account it should belong to on the target platform.
    /// When given, it is used in place of <paramref name="newPlayerId"/>/<paramref name="sourcePlayerId"/>.
    /// A character left out of the map keeps its existing id, same as leaving both of those
    /// null.</para>
    /// </summary>
    public static string SteamWorldToGamePass(
        string steamWorldDir, string destWgsDir, string? worldName = null, string? newPlayerId = null,
        bool mergeIntoExisting = false, string? sourcePlayerId = null,
        IReadOnlyDictionary<string, string>? rehomes = null)
    {
        if (!Directory.Exists(steamWorldDir))
        {
            throw new DirectoryNotFoundException($"Steam world folder not found: {steamWorldDir}");
        }
        worldName ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(steamWorldDir)));

        var saves = EnumerateWorldSaves(steamWorldDir).ToList();
        var playerIds = saves
            .Where(s => IsPlayerSave(s.Relative))
            .Select(s => PlayerIdentifier.TryParseFromPlayerFileName(s.Path, out var id) ? id : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
        var rehomeMap = ResolveRehomeMap(playerIds, newPlayerId, sourcePlayerId, rehomes);

        var members = new List<AbfMember>();
        var rehomedClaims = 0;
        foreach (var file in saves)
        {
            var bytes = File.ReadAllBytes(file.Path);
            var saveClass = ReadSaveClass(bytes);
            if (saveClass is null || !GamePassMemberCodec.IsEditableSaveClass(saveClass))
            {
                // Dropping a save here used to be a one-line log entry, which meant a corrupt or
                // newer-than-this-editor character simply vanished from the converted world and the
                // player found out when they went looking for it. Refuse the whole conversion instead.
                throw new InvalidDataException(
                    $"'{Path.GetFileName(file.Path)}' could not be read as an Abiotic Factor save"
                    + (saveClass is null ? "" : $" (its type is '{saveClass}')")
                    + ". Converting would leave it out of the new save, so nothing was written.");
            }

            // In-bundle member paths use forward slashes, no extension, under Profile/Worlds/<World>.
            var rel = file.Relative.Replace('\\', '/');
            if (rel.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) rel = rel[..^4];

            // Only characters named in the map change hands. Re-homing every character save in
            // sight would have packed a shared world's nine players over the top of each other,
            // all under one name.
            if (saveClass == GamePassMemberCodec.CharacterSaveClass
                && PlayerIdentifier.TryParseFromPlayerFileName(file.Path, out var thisPlayerId)
                && thisPlayerId is not null && rehomeMap.TryGetValue(thisPlayerId, out var newId))
            {
                bytes = StampOwner(bytes, newId);
                rel = $"PlayerData/Player_{newId}";
            }
            else if (!IsPlayerSave(file.Relative) && rehomeMap.Count > 0)
            {
                // The characters that moved accounts had their beds move with them too, or the
                // player arrives in their own base unable to sleep in their own bed. A Steam id
                // and an Xbox one are different lengths, which is exactly the case the patcher
                // handles by re-serializing rather than swapping bytes. Each mapping only ever
                // touches references to its own old id, so applying them one after another is
                // safe even with several characters re-homed in the same run.
                foreach (var (oldId, mappedId) in rehomeMap)
                {
                    bytes = WorldSteamIdPatcher.PatchBytes(bytes, oldId, mappedId, out var claims);
                    rehomedClaims += claims;
                }
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

        // The world's difficulty settings live beside the saves rather than inside them, and the
        // game packs them into the bundle as a text member. Leaving it out would quietly reset a
        // converted world to default difficulty.
        var sandbox = Path.Combine(steamWorldDir, SandboxSettingsFileName);
        if (File.Exists(sandbox))
        {
            members.Add(new AbfMember
            {
                Path = $"Profile/Worlds/{worldName}/{SandboxSettingsFileName}",
                SaveClass = string.Empty,
                Flag = AbfMember.IniFlag,
                Body = GamePassMemberCodec.EncodeIniText(File.ReadAllText(sandbox)),
            });
        }

        var containerName = $"{worldName}-WC";
        var blob = AbfSaveBundle.Create(members).Serialize();
        if (mergeIntoExisting)
        {
            WgsContainerStore.Open(destWgsDir).AddOrReplaceContainer(containerName, blob);
        }
        else
        {
            WgsContainerStore.WriteNewContainer(destWgsDir, containerName, blob);
        }
        Diagnostics.EditorLog.Info("GamePass",
            $"Converted Steam world '{worldName}' ({members.Count} member(s)"
            + (rehomedClaims > 0 ? $", {rehomedClaims} bed claim(s) re-homed" : "")
            + $") -> Game Pass container at {destWgsDir}.");
        return destWgsDir;
    }

    /// <summary>The world difficulty settings file that sits next to a world's saves.</summary>
    private const string SandboxSettingsFileName = "SandboxSettings.ini";

    /// <summary>
    /// The file a Game Pass -&gt; Steam conversion leaves behind naming the wgs folder it came
    /// from, so appearance editing can still reach the account-level customization containers
    /// that live only in the Game Pass container, never in the extracted loose files. This used
    /// to be inferred from the destination's own name and location (a fixed "&lt;wgs&gt;-Steam"
    /// sibling of the source), which broke once the destination stopped always being that -
    /// a marker records the real source directly instead of relying on where the copy happens
    /// to live.
    /// </summary>
    public const string SourceMarkerFileName = ".abiotic-gamepass-source";

    private static void WriteSourceMarker(string destSteamDir, string wgsDir)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(destSteamDir, SourceMarkerFileName),
                Path.GetFullPath(wgsDir));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort only: appearance editing falls back to walking the folder's ancestors
            // when this cannot be written, so a locked-down destination still converts fine.
            Diagnostics.EditorLog.Warn("GamePass", $"Could not record the Game Pass source next to '{destSteamDir}': {exception.Message}");
        }
    }

    /// <summary>Reads back what <see cref="WriteSourceMarker"/> recorded, if anything and if it
    /// still points at a real Game Pass folder. Read-only and best-effort.</summary>
    public static string? TryReadSourceMarker(string steamWorldDir)
    {
        try
        {
            var markerPath = Path.Combine(steamWorldDir, SourceMarkerFileName);
            if (!File.Exists(markerPath)) return null;
            var recorded = File.ReadAllText(markerPath).Trim();
            return recorded.Length > 0 && GamePassSaveSet.IsGamePassFolder(recorded) ? recorded : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The "nothing readable in this folder" error. A Game Pass world bundle is compressed with
    /// Oodle, which the editor borrows from the installed game or downloads once, so a folder can
    /// also look empty simply because that library could not be obtained. Saying "no world
    /// containers found" then sends people hunting for a problem with their save folder that does
    /// not exist, so the real reason is named instead. Any world container that failed to unpack
    /// for its own reason is reported with that reason.
    /// </summary>
    private static InvalidDataException NoContainers(string wgsDir, IReadOnlyList<GamePassContainerFault> faults)
    {
        if (faults.Count > 0)
        {
            var detail = string.Join("; ", faults.Select(f => $"{f.ContainerName}: {f.Message}"));
            return new InvalidDataException(
                $"The worlds in '{wgsDir}' could not be unpacked ({detail}).");
        }
        return OodleCodec.IsAvailable
            ? new InvalidDataException($"No world containers found in '{wgsDir}'.")
            : new InvalidDataException(
                $"Game Pass saves in '{wgsDir}' could not be unpacked because the Oodle compression "
                + "library is not available. The editor takes it from an installed copy of the game, "
                + "or downloads it once if you are online. Connect to the internet and try again, or "
                + "set ABIOTIC_OODLE_DLL to the path of a copy.");
    }

    /// <summary>
    /// Unpacks a Game Pass world container into a Steam world folder at
    /// <paramref name="destSteamDir"/> (loose <c>.sav</c> files). When <paramref name="containerName"/>
    /// is null the only world container is used. When <paramref name="newPlayerId"/> is set a
    /// character is re-homed to that account id so the world belongs to the target Steam account;
    /// leave it null to keep the existing ids. On a world with several characters say which one with
    /// <paramref name="sourcePlayerId"/>; the others are carried over untouched. To re-home more than
    /// one character in the same run, pass <paramref name="rehomes"/> instead (see
    /// <see cref="SteamWorldToGamePass"/> for its shape); when given it replaces
    /// <paramref name="newPlayerId"/>/<paramref name="sourcePlayerId"/>. Returns the world folder path.
    /// </summary>
    public static string GamePassToSteamWorld(
        string wgsDir, string? containerName, string destSteamDir, string? newPlayerId = null,
        string? sourcePlayerId = null, IReadOnlyDictionary<string, string>? rehomes = null)
    {
        GuardEmptyDestination(destSteamDir);
        var set = GamePassSaveSet.Open(wgsDir);
        var entries = set.Entries();
        var containers = entries.Select(e => e.ContainerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (containerName is null && containers.Count > 1)
        {
            throw new InvalidOperationException(
                $"'{wgsDir}' holds more than one world ({string.Join(", ", containers)}). "
                + "Say which one to convert.");
        }
        var container = containerName
            ?? containers.FirstOrDefault()
            ?? throw NoContainers(wgsDir, set.Faults);

        // Settle who is being re-homed BEFORE anything is written. Resolving after the extract
        // meant a world the editor was always going to refuse still left a full copy of itself in
        // the destination, which then failed the empty-destination check on the next attempt.
        var playerIds = set.EntriesForContainer(container)
            .Where(e => e.Kind == GamePassSaveKind.Player)
            .Select(e => PlayerIdentifier.TryParseFromPlayerFileName(e.FileName, out var id) ? id : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
        var rehomeMap = ResolveRehomeMap(playerIds, newPlayerId, sourcePlayerId, rehomes);

        set.ExtractWorld(container, destSteamDir);
        WriteSourceMarker(destSteamDir, wgsDir);

        foreach (var (oldPlayerId, newPlayerIdValue) in rehomeMap)
        {
            var source = Path.Combine(destSteamDir, "PlayerData", $"Player_{oldPlayerId}.sav");
            if (!File.Exists(source)) continue;

            PlayerSaveIdentity.ChangeSteamId(source, newPlayerIdValue);
            DeleteFreshExtractionBackup(source);

            // Beds and other claimable deployables record their owner inside the world saves, so
            // a character that changes account without this stays locked out of its own bed. An
            // Xbox id is 16 digits and a SteamID64 is 17, which is why this could not be done
            // until the patcher learned to re-serialize. Only this character's claims move; the
            // other players in a shared world keep theirs, even when several are re-homed in the
            // same run - each pass only ever touches references to its own old id.
            var claims = WorldSteamIdPatcher.PatchFolder(destSteamDir, oldPlayerId, newPlayerIdValue);
            if (claims > 0)
            {
                Diagnostics.EditorLog.Info("GamePass",
                    $"Re-homed {claims} bed claim(s) from {oldPlayerId} to {newPlayerIdValue}.");
            }
        }
        if (rehomeMap.Count > 0)
        {
            foreach (var world in Directory.EnumerateFiles(
                destSteamDir, "WorldSave_*.sav", SearchOption.TopDirectoryOnly))
            {
                DeleteFreshExtractionBackup(world);
            }
        }

        Diagnostics.EditorLog.Info("GamePass",
            $"Converted Game Pass container '{container}' -> Steam world folder at {destSteamDir}.");
        return destSteamDir;
    }

    /// <summary>
    /// True when converting this world without re-homing anybody would leave its characters owned
    /// by accounts the destination platform will never look for, so the game starts the player
    /// with a brand new character while their real one sits unopened in the folder.
    /// </summary>
    /// <remarks>
    /// <para>The game finds a character by file name: Steam looks for <c>Player_&lt;SteamID64&gt;.sav</c>
    /// and Game Pass for the Xbox account's. A conversion carries every character across intact, but
    /// under the id it already had, so a world that changes platform without an account id given
    /// arrives complete and unreachable. This is what a player reports as "it made me a new level 1
    /// character": nothing was lost, it is simply owned by the account they just left.</para>
    /// <para>Only the cases that really bite say true. Heading for Steam, a character already on a
    /// SteamID64 is fine as it is. Heading for Game Pass, a SteamID64 is definitely not an Xbox
    /// account, while any other token might be, so only the certain mismatch is called out: a
    /// warning that cries wolf on a correct conversion is worse than none.</para>
    /// </remarks>
    /// <param name="destination">The platform the world is being converted to.</param>
    /// <param name="characterIds">The world's character ids, from <see cref="ListContainerPlayers"/>
    /// or <see cref="ListSteamWorldPlayers"/>.</param>
    public static bool WouldStrandCharacters(Saves.SavePlatform destination, IEnumerable<string>? characterIds)
    {
        var ids = characterIds?.ToList() ?? [];
        if (ids.Count == 0) return false;

        return destination == Saves.SavePlatform.Steam
            ? !ids.Any(PlayerIdentifier.IsSteamId)
            : ids.Any(PlayerIdentifier.IsSteamId);
    }

    /// <summary>
    /// The account ids of the characters in a Game Pass world, so a caller can ask which one to
    /// re-home instead of guessing. <paramref name="containerName"/> may be null when the folder
    /// holds a single world.
    /// </summary>
    public static IReadOnlyList<string> ListContainerPlayers(string wgsDir, string? containerName = null)
    {
        var set = GamePassSaveSet.Open(wgsDir);
        var container = containerName
            ?? set.Entries().Select(e => e.ContainerName).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (container is null) return Array.Empty<string>();
        return set.EntriesForContainer(container)
            .Where(e => e.Kind == GamePassSaveKind.Player)
            .Select(e => PlayerIdentifier.TryParseFromPlayerFileName(e.FileName, out var id) ? id : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    /// <summary>The account ids of the characters in a loose Steam world folder. The counterpart of
    /// <see cref="ListContainerPlayers"/> for the other conversion direction.</summary>
    public static IReadOnlyList<string> ListSteamWorldPlayers(string steamWorldDir)
    {
        var playerData = Path.Combine(steamWorldDir, "PlayerData");
        if (!Directory.Exists(playerData)) return Array.Empty<string>();
        return Directory.EnumerateFiles(playerData, "Player_*.sav", SearchOption.TopDirectoryOnly)
            .Select(p => PlayerIdentifier.TryParseFromPlayerFileName(p, out var id) ? id : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    /// <summary>The <c>&lt;World&gt;-WC</c> containers in a wgs folder, so a caller can offer a choice
    /// instead of silently converting whichever one happens to come first.</summary>
    public static IReadOnlyList<string> ListWorldContainers(string wgsDir)
        => GamePassSaveSet.Open(wgsDir).Entries()
            .Select(e => e.ContainerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Refuses a destination that already holds saves. Conversion writes a world into this folder
    /// and, when re-homing, deletes the backup it made of the freshly extracted player - both of
    /// which are safe for a new folder and destructive for one with a world already in it.
    /// </summary>
    private static void GuardEmptyDestination(string destDir)
    {
        if (!Directory.Exists(destDir)) return;
        if (Directory.EnumerateFiles(destDir, "*.sav", SearchOption.AllDirectories).Any()
            || WgsContainerStore.IsContainerFolder(destDir))
        {
            throw new InvalidOperationException(
                $"'{destDir}' already contains a save. Converting into it could overwrite that world, "
                + "so nothing was written. Choose an empty folder.");
        }
    }

    private static bool IsPlayerSave(string relative)
        => Path.GetFileName(relative).StartsWith("Player_", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="savePath"/> is the character save for
    /// <paramref name="playerId"/>. File names are matched the way the filesystem would.</summary>
    private static bool IsPlayerFor(string savePath, string playerId)
        => PlayerIdentifier.TryParseFromPlayerFileName(savePath, out var id)
           && string.Equals(id, playerId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Drops the <c>.bak</c> the re-home left behind. Conversion writes into a brand-new folder,
    /// so the "previous" file it preserves is one the editor extracted seconds earlier, and
    /// leaving it would hand the player a world folder full of stale duplicates.
    /// </summary>
    private static void DeleteFreshExtractionBackup(string savePath)
    {
        var bak = savePath + ".bak";
        if (File.Exists(bak)) File.Delete(bak);
    }

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

    /// <summary>
    /// Works out which character a conversion is re-homing, or null when it is not re-homing one.
    ///
    /// <para>A shared world used to be refused outright, on the reasoning that one account id cannot
    /// own several characters. True, but the conclusion was wrong: the player only ever wanted
    /// <em>their own</em> character to become theirs on the new platform, and their friends'
    /// characters were never the target. Refusing meant a co-op world could not be moved at all - the
    /// game found no save for the player's account and offered character creation on top of a world
    /// they had 200 hours in. So several characters is fine as long as the caller says which one;
    /// only the ambiguity is refused, and the message lists the candidates so the answer is to hand.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The choice is ambiguous, names a character the
    /// world does not have, or would collide with a character already on the target id.</exception>
    private static readonly Dictionary<string, string> EmptyRehomeMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Works out the full set of characters being re-homed this run, keyed by their current
    /// account id. <paramref name="rehomes"/>, when given and non-empty, takes over from
    /// <paramref name="newPlayerId"/>/<paramref name="sourcePlayerId"/> entirely - it exists so a
    /// co-op world can convert everyone's character to their own new account in one pass, instead
    /// of the single-character shape those two parameters are limited to.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry names a character the world does not
    /// have, or two characters would collide on the same destination account.</exception>
    private static Dictionary<string, string> ResolveRehomeMap(
        List<string> playerIds, string? newPlayerId, string? sourcePlayerId,
        IReadOnlyDictionary<string, string>? rehomes)
    {
        if (rehomes is not { Count: > 0 })
        {
            var single = ResolveRehomeSource(
                playerIds, ValidateOptionalId(newPlayerId), ValidateOptionalId(sourcePlayerId));
            return single is null
                ? EmptyRehomeMap
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [single] = ValidateOptionalId(newPlayerId)! };
        }

        var listed = string.Join(", ", playerIds);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawSource, rawDest) in rehomes)
        {
            var source = ValidateOptionalId(rawSource)
                ?? throw new ArgumentException("A character to re-home was given with no account id.", nameof(rehomes));
            var dest = ValidateOptionalId(rawDest)
                ?? throw new ArgumentException($"No destination account id was given for '{source}'.", nameof(rehomes));
            if (!playerIds.Any(id => string.Equals(id, source, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"This world has no character '{source}'. It has: {listed}.");
            }
            // Re-homing a character to the id it already has is nothing to do.
            if (!string.Equals(source, dest, StringComparison.Ordinal)) map[source] = dest;
        }

        // Two characters under one account id would leave the game loading whichever it saw first
        // and the other one silently unreachable, so a collision is refused rather than resolved -
        // whether it is two characters both moving to the same account, or one moving onto an
        // account a third, untouched character already has.
        var collision = map.GroupBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"More than one character is being moved to account {collision.Key}, so one of them "
                + "would be hidden. Give each character a different account.");
        }
        var takenByBystander = map.Values.FirstOrDefault(dest =>
            playerIds.Any(id => string.Equals(id, dest, StringComparison.OrdinalIgnoreCase))
            && !map.ContainsKey(dest));
        if (takenByBystander is not null)
        {
            throw new InvalidOperationException(
                $"This world already has a character on account {takenByBystander}, so moving another "
                + "one there would hide it. Pick a different account id.");
        }

        return map;
    }

    private static string? ResolveRehomeSource(
        List<string> playerIds, string? newPlayerId, string? sourcePlayerId)
    {
        if (newPlayerId is null)
        {
            if (sourcePlayerId is not null)
            {
                throw new InvalidOperationException(
                    $"Nothing was said to re-home '{sourcePlayerId}' to. Give the new account id as well.");
            }
            return null;
        }
        if (playerIds.Count == 0) return null;

        var listed = string.Join(", ", playerIds);
        if (sourcePlayerId is null)
        {
            if (playerIds.Count > 1)
            {
                throw new InvalidOperationException(
                    $"This world has {playerIds.Count} characters, so say which one becomes "
                    + $"{newPlayerId}: {listed}. The others are carried over unchanged.");
            }
            sourcePlayerId = playerIds[0];
        }
        else if (!playerIds.Any(id => string.Equals(id, sourcePlayerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"This world has no character '{sourcePlayerId}'. It has: {listed}.");
        }

        // Two characters under one account id would leave the game loading whichever it saw first
        // and the other one silently unreachable, so a collision is refused rather than resolved.
        if (playerIds.Any(id => string.Equals(id, newPlayerId, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(sourcePlayerId, newPlayerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This world already has a character on account {newPlayerId}, so moving another one "
                + "there would hide it. Pick a different account id.");
        }

        // Re-homing a character to the id it already has is nothing to do.
        return string.Equals(sourcePlayerId, newPlayerId, StringComparison.Ordinal) ? null : sourcePlayerId;
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
