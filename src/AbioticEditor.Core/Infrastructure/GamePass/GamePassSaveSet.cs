using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

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
/// The game's own spare copy of a world: the <c>&lt;World&gt;-WC-B</c> container that sits beside
/// every <c>&lt;World&gt;-WC</c> in a Game Pass save.
/// </summary>
/// <remarks>
/// Confirmed against a real 8-container Xbox save: the <c>-WC-B</c> container is a complete world
/// bundle, not a fragment. It held all 70 members of its live twin (every region save, the
/// metadata, all nine player saves and the world's <c>SandboxSettings.ini</c>) under the same world
/// name, at an earlier generation - container.113 against container.170, with a slightly smaller
/// Facility region. So it is a full, loadable rolling backup one generation behind, which is
/// exactly what a player whose world has broken needs and could not previously see.
/// </remarks>
/// <param name="ContainerName">The backup container, e.g. <c>ForScience-WC-B</c>.</param>
/// <param name="WorldName">The world it is a backup of, e.g. <c>ForScience</c>.</param>
/// <param name="LiveContainerName">The world container it would be restored over.</param>
/// <param name="LiveWorldExists">False when the live world is gone and only this copy remains.</param>
/// <param name="BlobSize">Size of the packed backup in bytes.</param>
/// <param name="LastSavedUtc">When the game last refreshed this backup.</param>
public sealed record GamePassWorldBackup(
    string ContainerName,
    string WorldName,
    string LiveContainerName,
    bool LiveWorldExists,
    long BlobSize,
    DateTime LastSavedUtc);

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
    /// Everything known about whether an edit written now would survive: Xbox's own conflict flag,
    /// container states, and whether the game or the Xbox app is running. Callers that intend to
    /// save should ask first, so the player is warned before they do the work rather than after.
    /// </summary>
    public GamePassWriteCheck CheckWritable() => _store.CheckWritable();

    /// <summary>
    /// Records that the player has been shown what <see cref="CheckWritable"/> found and wants to
    /// save anyway. Applies to this open save only.
    /// </summary>
    /// <param name="acknowledgement">The accepted risk (see <see cref="GamePassWriteOverride"/>).</param>
    public void AllowUnsafeWrites(GamePassWriteOverride acknowledgement)
        => _store.AllowUnsafeWrites(acknowledgement);

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
            // World bundles are the "-WC" containers; "-WC-B" are the game's own backups (see
            // WorldBackups), others are profile/settings.
            if (!container.Name.EndsWith(WorldSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                entries.AddRange(EntriesForContainer(container.Name));
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("GamePass", $"Could not read bundle '{container.Name}': {ex.Message}");
                _faults.Add(new GamePassContainerFault(container.Name, ex.Message));
            }
        }
        return entries;
    }

    /// <summary>
    /// The saves packed in one container, whether it is a live world or one of the game's own
    /// backups (<see cref="WorldBackups"/>). Unlike <see cref="Entries"/> a failure here is thrown
    /// rather than recorded as a fault, because a caller naming a single container needs the reason
    /// it could not be opened, not an empty list.
    /// </summary>
    /// <param name="containerName">The wgs logical container, e.g. <c>ForScience-WC</c>.</param>
    public IReadOnlyList<GamePassSaveEntry> EntriesForContainer(string containerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        var bundle = LoadBundle(containerName);
        var world = WorldNameOf(containerName);
        return bundle.Members.Select(m => new GamePassSaveEntry
        {
            ContainerName = containerName,
            WorldName = world,
            MemberPath = m.Path,
            SaveClass = m.SaveClass,
            Kind = KindOf(m),
        }).ToList();
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
        // Ask before backing anything up: a refused save must leave the folder untouched, not
        // scatter a snapshot of it for a write that never happened.
        _store.EnsureWritable();
        BackupOnce();
        Member(entry).Body = GamePassMemberCodec.ToMemberBody(entry.SaveClass, editedGvas);
        Repack(entry.ContainerName);
    }

    /// <summary>
    /// Extracts every editable save in <paramref name="containerName"/> to <paramref name="destDir"/>
    /// as loose <c>.sav</c> files in the normal world layout (<c>WorldSave_*.sav</c> at the top,
    /// <c>PlayerData/Player_*.sav</c> underneath) so the standard folder editor can open them.
    /// Returns the world name. Works for one of the game's own backup containers
    /// (<see cref="WorldBackups"/>) too, which is how a player gets an intact copy of a broken
    /// world out without restoring over anything.
    /// </summary>
    public string ExtractWorld(string containerName, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string world = containerName;
        foreach (var entry in EntriesForContainer(containerName).Where(e => e.TravelsWithWorld))
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
        _store.EnsureWritable();
        BackupOnce();

        var members = EntriesForContainer(containerName).Where(e => e.TravelsWithWorld).ToList();

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
        _store.EnsureWritable();
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
        _store.EnsureWritable();

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

        // The beds this character claimed record the owner id inside the world members of the same
        // container, so they move here too - in the same repack, so the container can never carry a
        // character on one account whose bed still belongs to another.
        var claims = RehomeClaims(entry.ContainerName, entry.FileName, newAccountId);

        BackupOnce();
        Repack(entry.ContainerName);
        Diagnostics.EditorLog.Info("GamePass",
            $"Re-homed {entry.FileName} -> {newFileName} in '{entry.ContainerName}'"
            + (claims > 0 ? $", including {claims} bed claim(s)." : "."));
        return newFileName;
    }

    /// <summary>
    /// Moves every claim held by the account named in <paramref name="oldPlayerFileName"/> over to
    /// <paramref name="newAccountId"/> across the container's world members, in memory. Returns how
    /// many changed; the caller repacks. A world member that carries none is left byte-for-byte as
    /// it was rather than re-serialized, so a rename only rewrites the regions it has to.
    /// </summary>
    private int RehomeClaims(string containerName, string oldPlayerFileName, string newAccountId)
    {
        if (!PlayerIdentifier.TryParseFromPlayerFileName(oldPlayerFileName, out var oldAccountId)) return 0;

        var claims = 0;
        foreach (var world in EntriesForContainer(containerName)
                     .Where(e => e.Kind is GamePassSaveKind.World or GamePassSaveKind.WorldMetadata))
        {
            var worldMember = Member(world);
            var gvas = GamePassMemberCodec.ToGvas(world.SaveClass, worldMember.Body);
            var patched = WorldSteamIdPatcher.PatchBytes(gvas, oldAccountId, newAccountId, out var rewritten);
            if (rewritten == 0) continue;
            worldMember.Body = GamePassMemberCodec.ToMemberBody(world.SaveClass, patched);
            claims += rewritten;
        }
        return claims;
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

    private const string WorldSuffix = "-WC";
    private const string BackupSuffix = "-WC-B";

    /// <summary>
    /// The game's own spare copies of the worlds in this folder (the <c>-WC-B</c> containers).
    ///
    /// <para>These are kept separate from <see cref="Entries"/> on purpose. A backup holds the same
    /// world under the same name one generation back, so folding the two together would leave a
    /// player unable to tell which copy they were editing, which is a worse problem than not seeing
    /// the backup at all. Restore one deliberately with <see cref="RestoreWorldFromBackup"/>.</para>
    /// </summary>
    public IReadOnlyList<GamePassWorldBackup> WorldBackups()
    {
        var live = new HashSet<string>(
            _store.Containers.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var backups = new List<GamePassWorldBackup>();
        foreach (var container in _store.Containers)
        {
            if (!container.Name.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var world = container.Name[..^BackupSuffix.Length];
            var liveName = world + WorldSuffix;
            backups.Add(new GamePassWorldBackup(
                container.Name,
                world,
                liveName,
                live.Contains(liveName),
                container.BlobSize,
                SafeFileTime(container.FileTime)));
        }
        return backups;
    }

    /// <summary>
    /// Copies the game's own backup of a world over the live world container, so a world that has
    /// broken can be put back to the copy the game itself kept.
    ///
    /// <para>A recovery action, never part of a normal save: the live world's current contents are
    /// replaced wholesale by an older generation, and everything that happened since is gone. The
    /// whole wgs folder is backed up first (the <c>.bak</c> beside it), so the state being replaced
    /// is still recoverable afterwards.</para>
    /// </summary>
    /// <param name="backupContainerName">A container from <see cref="WorldBackups"/>, e.g. <c>ForScience-WC-B</c>.</param>
    /// <returns>The world that was restored.</returns>
    /// <exception cref="InvalidOperationException">No such backup container.</exception>
    /// <exception cref="InvalidDataException">The backup does not hold a world.</exception>
    public string RestoreWorldFromBackup(string backupContainerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupContainerName);
        if (!backupContainerName.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{backupContainerName}' is not one of the game's backup copies (those end in '-WC-B').");
        }
        var backup = _store.Find(backupContainerName)
            ?? throw new InvalidOperationException(
                $"This save has no backup copy called '{backupContainerName}'.");
        _store.EnsureWritable();

        var blob = _store.ReadBlob(backup);
        // Check before anything is written: restoring bytes that are not a world would replace a
        // damaged world with one the game cannot open at all.
        if (!AbfSaveBundle.LooksLikeBundle(blob))
        {
            throw new InvalidDataException(
                $"The backup copy '{backupContainerName}' does not hold a world, so it cannot be restored.");
        }

        var world = backupContainerName[..^BackupSuffix.Length];
        var liveName = world + WorldSuffix;
        BackupOnce();
        if (_store.Find(liveName) is { } liveContainer)
        {
            _store.WriteBlob(liveContainer, blob);
        }
        else
        {
            // The live world is gone from the index entirely, which is the case where this backup is
            // the only copy left. Adding it back is the whole point.
            _store.AddOrReplaceContainer(liveName, blob);
        }
        // The old contents are what any cached bundle still holds, and a save made from that cache
        // would put the broken world straight back.
        _bundles.Remove(liveName);
        Diagnostics.EditorLog.Info("GamePass",
            $"Restored '{liveName}' from the game's backup copy '{backupContainerName}' ({blob.Length} bytes).");
        return world;
    }

    /// <summary>
    /// Save data in this folder that the container list no longer points at - a world Xbox cloud
    /// sync dropped from the index while leaving it on disk. See
    /// <see cref="WgsContainerStore.FindOrphanedContainers"/>.
    /// </summary>
    public IReadOnlyList<WgsOrphanedContainer> OrphanedContainers() => _store.OrphanedContainers();

    /// <summary>
    /// Puts one leftover folder back into the container list so the game can see that world again.
    /// Backs up the whole wgs folder first. Only the container list changes: the save data stays
    /// exactly where it already is.
    /// </summary>
    /// <param name="orphan">One of the entries from <see cref="OrphanedContainers"/>.</param>
    /// <param name="containerName">The name to put it back under (default: the orphan's suggestion).</param>
    /// <returns>The name it was registered under.</returns>
    public string RecoverOrphanedWorld(WgsOrphanedContainer orphan, string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(orphan);
        _store.EnsureWritable();
        BackupOnce();
        return _store.ReRegisterOrphan(orphan, containerName).Name;
    }

    /// <summary>The world a container holds, with the world or backup suffix taken off.</summary>
    private static string WorldNameOf(string containerName)
    {
        if (containerName.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return containerName[..^BackupSuffix.Length];
        }
        return containerName.EndsWith(WorldSuffix, StringComparison.OrdinalIgnoreCase)
            ? containerName[..^WorldSuffix.Length]
            : containerName;
    }

    /// <summary>A container's FILETIME as a date, tolerating the out-of-range values a damaged
    /// index can carry rather than throwing while listing what is in a folder.</summary>
    private static DateTime SafeFileTime(long fileTime)
    {
        try { return DateTime.FromFileTimeUtc(fileTime); }
        catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
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
        _store.EnsureWritable();
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
