using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.GamePass;

/// <summary>The kind of editable save a Game Pass bundle member maps to.</summary>
public enum GamePassSaveKind
{
    Player,
    World,
    WorldMetadata,

    /// <summary>The world's <c>SandboxSettings.ini</c> (difficulty knobs) - text, not a GVAS save,
    /// so the save editors do not open it, but it travels with the world.</summary>
    SandboxSettings,
    Other,
}

/// <summary>
/// One editable save inside a Game Pass world bundle, presented as if it were a loose
/// <c>.sav</c> file so the existing readers/writers can consume it.
/// </summary>
public sealed class GamePassSaveEntry
{
    public required string ContainerName { get; init; }   // wgs logical container, e.g. "ForScience-WC"
    public required string WorldName { get; init; }       // e.g. "ForScience"
    public required string MemberPath { get; init; }      // in-bundle path
    public required string SaveClass { get; init; }
    public required GamePassSaveKind Kind { get; init; }

    /// <summary>The <c>Player_&lt;id&gt;.sav</c> / <c>WorldSave_*.sav</c> file name. In-bundle paths
    /// drop the extension, so it is re-added to match how the rest of the editor names saves. The
    /// ini member keeps its own extension.</summary>
    public string FileName
    {
        get
        {
            var name = Path.GetFileName(MemberPath.Replace('\\', '/'));
            if (Kind == GamePassSaveKind.SandboxSettings) return name;
            return name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? name : name + ".sav";
        }
    }

    /// <summary>True for members the editor can open (player + world GVAS saves).</summary>
    public bool IsEditable => Kind is not (GamePassSaveKind.Other or GamePassSaveKind.SandboxSettings);

    /// <summary>True for members that belong in an extracted world folder - the GVAS saves plus the
    /// world's <c>SandboxSettings.ini</c>, which is not editable here but must survive a
    /// round-trip rather than being dropped on the floor.</summary>
    public bool TravelsWithWorld => IsEditable || Kind == GamePassSaveKind.SandboxSettings;
}

/// <summary>A world container that could not be unpacked, and why.</summary>
/// <param name="ContainerName">The wgs logical container, e.g. <c>ForScience-WC</c>.</param>
/// <param name="Message">The failure as reported by the bundle/blob layer.</param>
public sealed record GamePassContainerFault(string ContainerName, string Message);

/// <summary>
/// High-level view of one Xbox "wgs" folder for Abiotic Factor: it surfaces the world/player saves
/// packed inside each world container (<c>&lt;World&gt;-WC</c>) as virtual <c>.sav</c> files, hands
/// the existing readers reconstructed GVAS bytes, and writes edits back through the bundle + wgs
/// layers (Oodle recompress, new blob generation) with a one-time backup of the whole folder.
/// </summary>
public sealed class GamePassSaveSet
{
    /// <summary>How many <c>.bak</c> snapshots of the wgs folder to keep. Each one is a full copy
    /// of the save folder, and they sit next to the player's real saves, so an unbounded series
    /// would quietly fill a drive with near-identical megabytes.</summary>
    private const int MaxBackups = 8;

    private readonly WgsContainerStore _store;
    private readonly Dictionary<string, AbfSaveBundle> _bundles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GamePassContainerFault> _faults = new();
    private bool _backedUp;

    public string FolderPath { get; }

    /// <summary>
    /// World containers that failed to unpack the last time <see cref="Entries"/> ran. They are
    /// skipped rather than aborting the whole folder (one broken world should not hide the others),
    /// but a caller that is about to act on "the world in this folder" must be able to tell the
    /// difference between a folder with one world and a folder where a second world failed to
    /// open.
    /// </summary>
    public IReadOnlyList<GamePassContainerFault> Faults => _faults;

    private GamePassSaveSet(string folder, WgsContainerStore store)
    {
        FolderPath = folder;
        _store = store;
    }

    /// <summary>True when <paramref name="folder"/> is a wgs container folder.</summary>
    public static bool IsGamePassFolder(string folder) => WgsContainerStore.IsContainerFolder(folder);

    /// <summary>
    /// True when opening/reading this save had to recover a container from a sibling blob because the
    /// one its manifest referenced was missing - a reliable sign Xbox cloud sync has not finished. In
    /// this state the save reads fine but writing risks Xbox discarding the edit, so the host should
    /// warn (or block writes) until sync settles. See <see cref="WgsContainerStore.NeededBlobFallback"/>.
    /// </summary>
    public bool IsMidSync => _store.NeededBlobFallback;

    /// <summary>The logical containers that had to be recovered from a fallback blob (empty when none).</summary>
    public IReadOnlyList<string> RecoveredContainers => _store.RecoveredContainers;

    /// <summary>
    /// True when Xbox is holding an unsettled conflict for this save, or when part of it carries a
    /// state no version of the format defines (which older builds of this editor wrote). Both mean
    /// the save is already in an argument with the cloud before the player changes anything, and
    /// both are reasons an edit can be thrown away later or the world can stop loading.
    /// </summary>
    public bool NeedsAttention => _store.HasUnresolvedConflicts || _store.InvalidStateContainers.Count > 0;

    /// <summary>True when Xbox has a conflict for this save it has not resolved.</summary>
    public bool HasUnresolvedConflicts => _store.HasUnresolvedConflicts;

    /// <summary>Parts of the save left in a state the format does not define.</summary>
    public IReadOnlyList<string> InvalidStateContainers => _store.InvalidStateContainers;

    /// <summary>
    /// Permanently repairs the mid-sync inconsistency (see <see cref="IsMidSync"/>) by pointing each
    /// recovered container's manifest at the blob that exists on disk. Backs up the whole wgs folder
    /// once first. After this, <see cref="IsMidSync"/> is false and the save reopens cleanly. Returns
    /// the container names repaired. Intended to be run with the game and Xbox app closed.
    /// </summary>
    public IReadOnlyList<string> RepairMidSync()
    {
        BackupOnce();
        return _store.RepairRecoveredManifests();
    }

    public static GamePassSaveSet Open(string folder)
    {
        var store = WgsContainerStore.Open(folder);
        return new GamePassSaveSet(folder, store);
    }

    /// <summary>Every editable player/world save across all world containers in this folder.</summary>
    public IReadOnlyList<GamePassSaveEntry> Entries()
    {
        var entries = new List<GamePassSaveEntry>();
        _faults.Clear();
        foreach (var container in _store.Containers)
        {
            // World bundles are the "-WC" containers; "-WC-B" are backups, others are profile/settings.
            if (!container.Name.EndsWith("-WC", StringComparison.OrdinalIgnoreCase)) continue;

            AbfSaveBundle bundle;
            try
            {
                bundle = LoadBundle(container.Name);
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("GamePass", $"Could not read bundle '{container.Name}': {ex.Message}");
                _faults.Add(new GamePassContainerFault(container.Name, ex.Message));
                continue;
            }

            var world = container.Name[..^"-WC".Length];
            foreach (var m in bundle.Members)
            {
                entries.Add(new GamePassSaveEntry
                {
                    ContainerName = container.Name,
                    WorldName = world,
                    MemberPath = m.Path,
                    SaveClass = m.SaveClass,
                    Kind = KindOf(m),
                });
            }
        }
        return entries;
    }

    /// <summary>Reconstructs a full GVAS save the editor can parse for the given entry.</summary>
    public byte[] ReadSave(GamePassSaveEntry entry)
    {
        var member = Member(entry);
        return GamePassMemberCodec.ToGvas(entry.SaveClass, member.Body);
    }

    /// <summary>
    /// Writes an edited GVAS save back: strips it to a member body, repacks the world bundle
    /// (Oodle), and writes a new blob generation. Backs up the whole wgs folder on first write.
    /// </summary>
    public void WriteSave(GamePassSaveEntry entry, byte[] editedGvas)
    {
        ArgumentNullException.ThrowIfNull(editedGvas);
        BackupOnce();
        Member(entry).Body = GamePassMemberCodec.ToMemberBody(entry.SaveClass, editedGvas);
        Repack(entry.ContainerName);
    }

    /// <summary>
    /// Extracts every editable save in <paramref name="containerName"/> to <paramref name="destDir"/>
    /// as loose <c>.sav</c> files in the normal world layout (<c>WorldSave_*.sav</c> at the top,
    /// <c>PlayerData/Player_*.sav</c> underneath) so the standard folder editor can open them.
    /// Returns the world name.
    /// </summary>
    public string ExtractWorld(string containerName, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string world = containerName;
        foreach (var entry in Entries().Where(e => e.ContainerName.Equals(containerName, StringComparison.OrdinalIgnoreCase) && e.TravelsWithWorld))
        {
            world = entry.WorldName;
            var path = ResolveMemberPath(entry, destDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (entry.Kind == GamePassSaveKind.SandboxSettings)
            {
                File.WriteAllText(path, GamePassMemberCodec.DecodeIniText(Member(entry).Body));
                continue;
            }
            File.WriteAllBytes(path, ReadSave(entry));
        }
        // Nothing was extracted for this container. Call LoadBundle directly so the real cause
        // (missing blob, bad Oodle stream, etc.) propagates to the caller instead of silently
        // producing an empty folder that shows as a blank sidebar.
        if (string.Equals(world, containerName, StringComparison.OrdinalIgnoreCase))
        {
            LoadBundle(containerName); // rethrows the bundle-load error if one occurred
            throw new InvalidOperationException($"Container '{containerName}' has no editable saves.");
        }
        return world;
    }

    /// <summary>
    /// Re-packs every edited save under <paramref name="srcDir"/> (as laid out by
    /// <see cref="ExtractWorld"/>) back into <paramref name="containerName"/> in one pass: each
    /// member body is refreshed from disk, any staged player rename is applied, the bundle is
    /// Oodle-recompressed once, and a single new blob generation is written. Backs up the wgs
    /// folder on first write. Returns the number of members written.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Not one member of the container was found in <paramref name="srcDir"/>. That means the
    /// working copy is not the one this container was extracted to (moved, cleaned up, or
    /// renamed), and packing would write the container back unchanged - so the caller's save
    /// would silently do nothing. Failing here is what lets the host keep the edit and say so.
    /// </exception>
    public int ApplyWorld(string containerName, string srcDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(srcDir);
        BackupOnce();

        var members = Entries()
            .Where(e => e.ContainerName.Equals(containerName, StringComparison.OrdinalIgnoreCase) && e.TravelsWithWorld)
            .ToList();

        var changed = 0;
        var missing = new List<string>();
        foreach (var entry in members)
        {
            var path = ResolveMemberPath(entry, srcDir);
            if (!File.Exists(path)) { missing.Add(entry.FileName); continue; }
            Member(entry).Body = entry.Kind == GamePassSaveKind.SandboxSettings
                ? GamePassMemberCodec.EncodeIniText(File.ReadAllText(path))
                : GamePassMemberCodec.ToMemberBody(entry.SaveClass, File.ReadAllBytes(path));
            changed++;
        }

        if (changed == 0)
        {
            throw new InvalidOperationException(
                $"None of the saves for '{containerName}' were found in '{srcDir}', so there was nothing "
                + "to pack back into the Xbox save. The working copy of this world is missing; reopen the "
                + "save and make the change again.");
        }
        if (missing.Count > 0)
        {
            Diagnostics.EditorLog.Warn("GamePass",
                $"Packing '{containerName}': {missing.Count} member(s) were not in the working copy and keep "
                + $"their previous contents ({string.Join(", ", missing)}).");
        }

        Repack(containerName);
        return changed;
    }

    /// <summary>
    /// Renames a player save inside <paramref name="containerName"/>'s bundle in memory, so
    /// re-homing a player to another account id survives the next repack. Nothing is written to
    /// the player's Xbox save until <see cref="ApplyWorld"/> or <see cref="WriteSave"/> runs, which
    /// keeps a rename part of the same SAVE as the edits around it instead of a separate write the
    /// player never asked for (and which would leave the container half-renamed if the save that
    /// followed it failed).
    ///
    /// <para>The rename has to reach the bundle at all because <see cref="ApplyWorld"/> walks the
    /// bundle's own table of contents and looks on disk for each member's recorded name: a file
    /// renamed only in the working copy simply stops being found, and the container keeps the old
    /// player under the old id.</para>
    /// </summary>
    /// <returns>True when a member was renamed.</returns>
    /// <exception cref="InvalidOperationException">The new name is already taken in this bundle.</exception>
    public bool StagePlayerRename(string containerName, string oldFileName, string newFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFileName);
        // Callers name saves the way the rest of the editor does, with the extension. Paths
        // inside the bundle leave it off, so both sides are compared without it.
        var oldMemberName = WithoutSavExtension(oldFileName);
        var newMemberName = WithoutSavExtension(newFileName);
        if (string.Equals(oldMemberName, newMemberName, StringComparison.OrdinalIgnoreCase)) return false;

        var bundle = LoadBundle(containerName);
        var member = bundle.Members.FirstOrDefault(m => WithoutSavExtension(m.Name).Equals(oldMemberName, StringComparison.OrdinalIgnoreCase));
        if (member is null) return false;
        if (bundle.Members.Any(m => WithoutSavExtension(m.Name).Equals(newMemberName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"'{newFileName}' already exists in this Game Pass world.");

        // Keep the member's own folder; only the file name at the end of it changes.
        var separator = member.Path.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        var lastSeparator = member.Path.LastIndexOfAny(['\\', '/']);
        member.Path = lastSeparator < 0
            ? newMemberName
            : string.Concat(member.Path.AsSpan(0, lastSeparator), separator.ToString(), newMemberName);
        return true;
    }

    /// <summary>
    /// Stages a player rename (see <see cref="StagePlayerRename"/>) and immediately writes the
    /// container, for callers with no later save step of their own (the command line).
    /// </summary>
    /// <returns>True when a member was renamed and the container rewritten.</returns>
    public bool RenamePlayerSave(string containerName, string oldFileName, string newFileName)
    {
        if (!StagePlayerRename(containerName, oldFileName, newFileName)) return false;
        BackupOnce();
        Repack(containerName);
        return true;
    }

    /// <summary>
    /// Re-homes a packed player save to <paramref name="newAccountId"/> and writes the container
    /// once. An owner id lives in two places - the save's file name and the <c>SaveIdentifier</c>
    /// inside it - and both are rewritten here in a single repack, so the container can never end
    /// up carrying a member named for one account whose contents claim another.
    /// </summary>
    /// <returns>The new file name.</returns>
    public string RenamePlayerToAccount(GamePassSaveEntry entry, string newAccountId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(newAccountId);
        if (entry.Kind != GamePassSaveKind.Player)
        {
            throw new InvalidOperationException($"'{entry.FileName}' is not a player save.");
        }
        if (!PlayerIdentifier.IsSafeFileToken(newAccountId))
        {
            throw new ArgumentException(
                $"'{newAccountId}' is not a valid account id (use letters, digits, '-', '_' or '.').",
                nameof(newAccountId));
        }

        var newFileName = $"Player_{newAccountId}.sav";
        var member = Member(entry);

        // Rewrite the owner id inside the save first: if the name is already taken the rename below
        // throws and nothing has been written to the player's container yet.
        var save = UeSaveGame.SaveGame.LoadFrom(new MemoryStream(GamePassMemberCodec.ToGvas(entry.SaveClass, member.Body), writable: false));
        PlayerSaveIdentity.StampIdentifier(save, newAccountId);
        using var restamped = new MemoryStream();
        save.WriteTo(restamped);
        var newBody = GamePassMemberCodec.ToMemberBody(entry.SaveClass, restamped.ToArray());

        if (!StagePlayerRename(entry.ContainerName, entry.FileName, newFileName))
        {
            throw new InvalidOperationException(
                $"'{entry.FileName}' is already owned by {newAccountId}.");
        }
        member.Body = newBody;

        BackupOnce();
        Repack(entry.ContainerName);
        Diagnostics.EditorLog.Info("GamePass",
            $"Re-homed {entry.FileName} -> {newFileName} in '{entry.ContainerName}'.");
        return newFileName;
    }

    private static string WithoutSavExtension(string name)
        => name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>
    /// Builds the working-copy path for a member and validates it stays inside
    /// <paramref name="baseDir"/>. The member's name comes from a bundle TOC path inside a save the
    /// user opened, so it is untrusted; this guards against a crafted container writing outside the
    /// working folder (zip-slip), on top of the leaf-only <see cref="GamePassSaveEntry.FileName"/>.
    /// Not hypothetical for the ini member in particular: the game records it under the absolute
    /// Windows path it had on the machine that wrote the save.
    /// </summary>
    private static string ResolveMemberPath(GamePassSaveEntry entry, string baseDir)
    {
        var relative = entry.Kind == GamePassSaveKind.Player
            ? Path.Combine("PlayerData", entry.FileName)
            : entry.FileName;
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Game Pass member '{entry.MemberPath}' has an unsafe name - extraction aborted.");
        }
        var root = Path.GetFullPath(baseDir);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Game Pass member '{entry.FileName}' resolves outside the working directory - extraction aborted.");
        }
        return full;
    }

    private void Repack(string containerName)
    {
        var blob = _bundles[containerName].Serialize();
        var container = _store.Find(containerName)
            ?? throw new InvalidOperationException($"Container '{containerName}' vanished.");
        _store.WriteBlob(container, blob);
    }

    private AbfSaveBundle LoadBundle(string containerName)
    {
        if (_bundles.TryGetValue(containerName, out var cached)) return cached;
        var container = _store.Find(containerName)
            ?? throw new InvalidOperationException($"No container '{containerName}'.");
        var blob = _store.ReadBlob(container);
        if (!AbfSaveBundle.LooksLikeBundle(blob))
        {
            throw new InvalidDataException($"Container '{containerName}' is not an ABF_SAVE_VERSION bundle.");
        }
        var bundle = AbfSaveBundle.Parse(blob);
        _bundles[containerName] = bundle;
        return bundle;
    }

    private AbfMember Member(GamePassSaveEntry entry)
    {
        var bundle = LoadBundle(entry.ContainerName);
        return bundle.Members.FirstOrDefault(m =>
                   string.Equals(m.Path, entry.MemberPath, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Member '{entry.MemberPath}' not found.");
    }

    private const string ProfileCustomizationPrefix = "ProfileScientistCustomization_";

    /// <summary>
    /// Returns the character slot numbers that have a
    /// <c>ProfileScientistCustomization_&lt;n&gt;</c> container in this wgs folder, sorted
    /// ascending. These are the Game Pass equivalent of the per-Steam-account
    /// <c>ScientistCustomization_&lt;n&gt;.sav</c> files.
    /// </summary>
    public IReadOnlyList<int> CustomizationSlots()
    {
        var slots = new List<int>();
        foreach (var c in _store.Containers)
        {
            if (!c.Name.StartsWith(ProfileCustomizationPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = c.Name[ProfileCustomizationPrefix.Length..];
            if (int.TryParse(suffix, out var slot)) slots.Add(slot);
        }
        slots.Sort();
        return slots;
    }

    /// <summary>
    /// Reads the raw GVAS bytes of the <c>ProfileScientistCustomization_&lt;slot&gt;</c>
    /// container, or null when the container does not exist (character never customized in-game).
    /// </summary>
    public byte[]? ReadProfileCustomization(int slot)
    {
        var container = _store.Find($"{ProfileCustomizationPrefix}{slot}");
        return container is null ? null : _store.ReadBlob(container);
    }

    /// <summary>
    /// Writes updated GVAS bytes back into the <c>ProfileScientistCustomization_&lt;slot&gt;</c>
    /// container (new blob generation). Backs up the whole wgs folder on the first write.
    /// </summary>
    public void WriteProfileCustomization(int slot, byte[] gvasBytes)
    {
        BackupOnce();
        var name = $"{ProfileCustomizationPrefix}{slot}";
        var container = _store.Find(name)
            ?? throw new InvalidOperationException($"Container '{name}' not found in this wgs folder.");
        _store.WriteBlob(container, gvasBytes);
        Diagnostics.EditorLog.Info("GamePass", $"Wrote profile customization slot {slot} ({gvasBytes.Length} bytes).");
    }

    private void BackupOnce()
    {
        if (_backedUp) return;
        var baseDest = FolderPath.TrimEnd('/', '\\') + ".bak";
        var dest = baseDest;
        if (Directory.Exists(dest))
        {
            dest += "-" + DateTime.UtcNow.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        CopyDirectory(FolderPath, dest);
        _backedUp = true;
        Diagnostics.EditorLog.Info("GamePass", $"Backed up wgs folder to {dest}");
        PruneOldBackups(baseDest);
    }

    /// <summary>
    /// Keeps the <see cref="MaxBackups"/> most recent snapshots and deletes the rest. Each editing
    /// session makes one, so without this an often-edited save accumulates full copies of itself
    /// indefinitely, next to the real saves. Best-effort: failing to delete an old backup must
    /// never fail the save it was taken for.
    /// </summary>
    private static void PruneOldBackups(string baseDest)
    {
        try
        {
            var parent = Path.GetDirectoryName(baseDest);
            if (string.IsNullOrEmpty(parent)) return;
            var prefix = Path.GetFileName(baseDest);

            var backups = Directory.EnumerateDirectories(parent, prefix + "*")
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .Skip(MaxBackups)
                .ToList();

            foreach (var old in backups)
            {
                try
                {
                    old.Delete(recursive: true);
                    Diagnostics.EditorLog.Info("GamePass", $"Removed old save backup {old.Name}.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diagnostics.EditorLog.Warn("GamePass", $"Could not remove old backup '{old.Name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("GamePass", $"Could not tidy old save backups: {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        // Rebuild each path from its position under the source root. A plain string replace
        // corrupts any path where the source folder's name occurs again further down.
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(dest, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private static GamePassSaveKind KindOf(AbfMember member) => member.IsIni
        ? GamePassSaveKind.SandboxSettings
        : member.SaveClass switch
        {
            GamePassMemberCodec.CharacterSaveClass => GamePassSaveKind.Player,
            GamePassMemberCodec.WorldSaveClass => GamePassSaveKind.World,
            GamePassMemberCodec.WorldMetadataSaveClass => GamePassSaveKind.WorldMetadata,
            _ => GamePassSaveKind.Other,
        };
}
