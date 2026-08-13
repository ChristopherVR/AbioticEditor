using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Holds the read-only save workspace currently open in a host.  The service has no UI
/// dependencies, so Razor components and future native hosts can share the same open,
/// selection, and summary workflow.
/// </summary>
public sealed class SaveWorkspaceSessionService : IDisposable
{
    /// <summary>
    /// How much of a save to read when identifying it during discovery. The class name sits in
    /// the GVAS header, after the engine version and the custom-format table, which is a few
    /// hundred bytes at most; this leaves generous room without ever touching the body.
    /// </summary>
    private const int HeaderProbeBytes = 8 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RecipeVocabularyService _recipeVocabulary;
    private readonly ItemUpgradeVocabularyService _itemUpgradeVocabulary;
    private readonly ProgressionVocabularyService _progressionVocabulary;
    private readonly CodexVocabularyService _codexVocabulary;
    private readonly HostLanguageService? _language;
    private readonly ISaveFileSystem _files;

    public SaveWorkspaceSessionService(RecipeVocabularyService recipeVocabulary, ProgressionVocabularyService progressionVocabulary, CodexVocabularyService codexVocabulary, ISaveFileSystem files)
        : this(recipeVocabulary, new ItemUpgradeVocabularyService(), progressionVocabulary, codexVocabulary, files) { }

    public SaveWorkspaceSessionService(RecipeVocabularyService recipeVocabulary, ItemUpgradeVocabularyService itemUpgradeVocabulary, ProgressionVocabularyService progressionVocabulary, CodexVocabularyService codexVocabulary, ISaveFileSystem files, HostLanguageService? language = null)
    {
        _recipeVocabulary = recipeVocabulary;
        _itemUpgradeVocabulary = itemUpgradeVocabulary;
        _progressionVocabulary = progressionVocabulary;
        _codexVocabulary = codexVocabulary;
        _files = files;
        _language = language;
    }

    /// <summary>
    /// True when this workspace's saves have real local paths, so features that hand a path to
    /// something outside the editor (revealing a file, the JSON side-car, Game Pass packing) can
    /// be offered. False in the browser.
    /// </summary>
    public bool HasLocalPaths => _files.HasLocalPaths;

    /// <summary>
    /// False when the open folder can only be read, which is what a browser without the File
    /// System Access API gets. Everything still opens and edits; the result leaves through EXPORT
    /// rather than SAVE, so screens offering a save action must check this first.
    /// </summary>
    public bool CanWrite => _files.CanWrite;

    /// <summary>The currently open workspace, or <see langword="null"/> before a folder is opened.</summary>
    public SaveWorkspace? Current { get; private set; }
    /// <summary>Most recently opened player session in this workspace, retained for staged container transfers.</summary>
    public PlayerSaveSession? TransferPlayerSession { get; private set; }
    /// <summary>Most recently opened world session, retained for staged carried-pet placement.</summary>
    public WorldSaveSession? TransferWorldSession { get; private set; }
    public string? BusyOperation { get; private set; }
    public event Action? Changed;

    /// <summary>
    /// Puts a message in the shell's busy line and gives the page a turn to draw it.
    /// </summary>
    /// <remarks>
    /// <para>The yield is the important half. A browser runs the editor on the same single thread
    /// it draws with, so setting a busy message and then getting straight on with the work meant
    /// the message never appeared: the render sat queued behind seconds of work and the page was
    /// simply frozen. Awaiting here hands control back long enough for the message to land.</para>
    ///
    /// <para>Public because some of the slowest work happens before there is a workspace to speak
    /// of - unpacking a dropped zip, most of all - and that still needs to say what it is doing.
    /// Pass null to clear.</para>
    /// </remarks>
    public async Task ReportBusyAsync(string? message)
    {
        BusyOperation = message;
        Changed?.Invoke();
        await UiBreather.BreatheAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Announces that an open session was edited in place. Editor tabs mutate the session
    /// models directly, so without this the shell (a sibling component, not an ancestor)
    /// never re-reads IsDirty and keeps showing SAVED STATE until an unrelated interaction.
    /// </summary>
    public void NotifyEdited() => Changed?.Invoke();

    /// <summary>Opens a world-save folder and discovers its player and world save files.</summary>
    public Task<SaveWorkspace> OpenAsync(string worldFolder, CancellationToken cancellationToken = default)
        => OpenAsync(worldFolder, platform: null, source: null, cancellationToken);

    /// <summary>Opens a discovered loose-file world while retaining its storefront badge.</summary>
    public Task<SaveWorkspace> OpenAsync(DiscoveredWorld world, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.IsGamePassContainer)
            return OpenGamePassAsync(world.FolderPath, world.GamePassContainer!, world.Source, cancellationToken);
        SavePlatform? platform = world.Platform == SavePlatform.Unknown ? null : world.Platform;
        return OpenAsync(world.FolderPath, platform, world.Source, cancellationToken);
    }

    /// <summary>
    /// Opens a Game Pass container world the way the native app does: the container is
    /// extracted to a temp working copy of loose .sav files that the normal folder editor
    /// operates on, and <see cref="SaveSelectedAsync"/> packs edits straight back into the
    /// Xbox container after every save.
    /// </summary>
    public async Task<SaveWorkspace> OpenGamePassAsync(
        string wgsFolder,
        string container,
        DiscoveredWorldSource? source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wgsFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BusyOperation = "Extracting the Game Pass save…"; Changed?.Invoke();
            var previousWorkingDir = Current?.GamePass?.WorkingDir;
            var session = await Task.Run(() =>
            {
                var set = GamePassSaveSet.Open(wgsFolder);
                var working = Path.Combine(Path.GetTempPath(), "AbioticEditor", "GamePass",
                    $"{container}-{Guid.NewGuid():N}");
                var worldName = set.ExtractWorld(container, working);
                return new GamePassWorkspaceSession(set, container, worldName, wgsFolder, working);
            }, cancellationToken).ConfigureAwait(false);
            var saves = await DiscoverSavesAsync(session.WorkingDir, cancellationToken).ConfigureAwait(false);

            if (session.Set.IsMidSync)
            {
                EditorLog.Warn("GamePass",
                    $"Opened '{container}' but it is mid-sync (recovered: {string.Join(", ", session.Set.RecoveredContainers)}).");
            }

            DeleteWorkingDir(previousWorkingDir);
            TransferPlayerSession = null;
            TransferWorldSession = null;
            Current = new SaveWorkspace(
                session.WorkingDir, saves, null, null, null, null, SavePlatform.GamePass, source)
            {
                GamePass = session,
            };
            EditorLog.Info("GamePass", $"Opened container '{container}' -> working copy {session.WorkingDir}");
            return Current;
        }
        finally
        {
            BusyOperation = null; Changed?.Invoke(); _gate.Release();
        }
    }

    /// <summary>Opens a working folder whose storefront is already known by the caller.</summary>
    public Task<SaveWorkspace> OpenAsync(string worldFolder, SavePlatform platform, CancellationToken cancellationToken = default)
        => OpenAsync(worldFolder, platform, source: null, cancellationToken);

    private async Task<SaveWorkspace> OpenAsync(
        string worldFolder,
        SavePlatform? platform,
        DiscoveredWorldSource? source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(worldFolder))
            throw new ArgumentException("A world save folder is required.", nameof(worldFolder));

        // Only a local path can be normalized; a browser folder handle's name is already the
        // only identifier there is.
        var fullPath = _files.HasLocalPaths ? Path.GetFullPath(worldFolder) : worldFolder;
        if (!await _files.FolderExistsAsync(fullPath, cancellationToken).ConfigureAwait(false))
            throw new DirectoryNotFoundException($"The world save folder does not exist: {fullPath}");

        // A picked/dropped wgs container folder has no loose .sav files, so route it through
        // the Game Pass extract flow instead of opening an empty workspace. Game Pass containers
        // are read with the local file system directly, so this only applies to hosts that have
        // one; a browser never sees an Xbox container folder in the first place.
        if (_files.HasLocalPaths && GamePassSaveSet.IsGamePassFolder(fullPath))
        {
            var container = await Task.Run(
                () => GamePassSaveSet.Open(fullPath).Entries()
                    .Select(entry => entry.ContainerName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(),
                cancellationToken).ConfigureAwait(false);
            if (container is null)
                throw new InvalidOperationException("This Game Pass folder contains no world saves.");
            return await OpenGamePassAsync(fullPath, container, source, cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReportBusyAsync("Scanning save folder…").ConfigureAwait(false);
            var previousWorkingDir = Current?.GamePass?.WorkingDir;
            var saves = await DiscoverSavesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            DeleteWorkingDir(previousWorkingDir);
            TransferPlayerSession = null;
            TransferWorldSession = null;
            Current = new SaveWorkspace(
                fullPath,
                saves,
                null,
                null,
                null,
                null,
                platform ?? InferPlatform(fullPath, saves),
                source);
            return Current;
        }
        finally
        {
            BusyOperation = null; Changed?.Invoke(); _gate.Release();
        }
    }

    /// <summary>Selects an already-discovered player or world save and reads its typed summary.</summary>
    public async Task<SaveWorkspace> SelectAsync(string savePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(savePath))
            throw new ArgumentException("A save file is required.", nameof(savePath));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workspace = Current ?? throw new InvalidOperationException("Open a world save folder before selecting a save.");
            // Only a local path can be normalized. A browser identifier ("Cascade/PlayerData/
            // Player_x.sav") is not a path at all, and GetFullPath would rewrite it into an
            // absolute one that matches nothing in the workspace - which is exactly how every
            // save in the browser became unselectable.
            var fullPath = _files.HasLocalPaths ? Path.GetFullPath(savePath) : savePath;
            var save = workspace.Saves.FirstOrDefault(s => string.Equals(s.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The selected save is not part of the open workspace.");

            BusyOperation = $"Loading {save.Name}…"; Changed?.Invoke();
            // Publish selection immediately so the sidebar and editor surface acknowledge
            // the click while parsing continues on a worker thread.
            Current = workspace with
            {
                SelectedSave = save,
                Summary = null,
                PlayerSession = null,
                WorldSession = null,
            };
            Changed?.Invoke();
            var selection = await ReadSelectionAsync(save, cancellationToken).ConfigureAwait(false);
            if (selection.PlayerSession is not null) TransferPlayerSession = selection.PlayerSession;
            if (selection.WorldSession is not null) TransferWorldSession = selection.WorldSession;
            Current = workspace with
            {
                SelectedSave = save,
                Summary = selection.Summary,
                PlayerSession = selection.PlayerSession,
                WorldSession = selection.WorldSession,
            };
            return Current;
        }
        finally
        {
            BusyOperation = null; Changed?.Invoke(); _gate.Release();
        }
    }

    public async Task ReloadSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (Current?.SelectedSave is not { } save) return;

        // A Game Pass workspace reads from a temp extraction, so a reload must first re-open
        // the wgs container from disk (picking up anything Xbox sync wrote) and re-extract
        // fresh bytes into the working copy before the file is re-parsed.
        if (Current?.GamePass is { } gamePass)
        {
            BusyOperation = "Reloading the Game Pass save…"; Changed?.Invoke();
            try
            {
                var freshSet = await Task.Run(() =>
                {
                    var set = GamePassSaveSet.Open(gamePass.WgsFolder);
                    set.ExtractWorld(gamePass.Container, gamePass.WorkingDir);
                    return set;
                }, cancellationToken).ConfigureAwait(false);
                if (Current?.GamePass == gamePass)
                    Current = Current with { GamePass = gamePass with { Set = freshSet } };
            }
            finally
            {
                BusyOperation = null; Changed?.Invoke();
            }
        }

        await SelectAsync(save.Path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-homes the selected player to a new account id, then rebuilds the workspace
    /// selection around the renamed file. Core rewrites both the filename and the
    /// embedded SaveIdentifier and preserves the original as a backup, and the beds this
    /// character claimed are moved to the new id in the world saves as well.
    /// </summary>
    public async Task ChangeSelectedPlayerIdentifierAsync(string newIdentifier, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workspace = Current ?? throw new InvalidOperationException("Open a world save folder first.");
            var player = workspace.PlayerSession ?? throw new InvalidOperationException("Select a player save first.");
            if (player.IsDirty) throw new InvalidOperationException("Save or revert staged player changes before changing the player ID.");

            BusyOperation = "Changing player ID..."; Changed?.Invoke();
            var oldFileName = Path.GetFileName(player.Path);
            // The id this character is leaving lives in the file name, so it has to be read before
            // the rename or the beds it claimed can no longer be found.
            var hasOldIdentifier = PlayerIdentifier.TryParseFromPlayerFileName(player.Path, out var oldIdentifier);

            // A Game Pass world is edited through an unpacked copy, and the repack walks the
            // container's own list of names, so the rename has to reach the container too or the
            // new id never lands and the old player quietly comes back. Stage it there BEFORE
            // touching the working copy: staging is the step that can refuse (the id is already
            // taken), and doing it first means a refusal leaves both sides untouched instead of a
            // renamed file whose container still knows the old name. Nothing is written to the
            // player's Xbox saves until the next SAVE.
            var newFileName = $"Player_{newIdentifier}.sav";
            var staged = false;
            if (workspace.GamePass is { } gamePassRename)
            {
                staged = await Task.Run(
                    () => gamePassRename.Set.StagePlayerRename(gamePassRename.Container, oldFileName, newFileName),
                    cancellationToken).ConfigureAwait(false);
            }

            string newPath;
            try
            {
                newPath = await Task.Run(() => PlayerSaveIdentity.ChangeSteamId(player.Path, newIdentifier), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The container now expects a name the working copy does not have. Left alone, the
                // next save would write that name over the OLD character's data - a player renamed
                // in the list but not in fact. Put the staged name back so the two agree again.
                if (staged && workspace.GamePass is { } undo)
                {
                    undo.Set.StagePlayerRename(undo.Container, newFileName, oldFileName);
                }
                throw;
            }

            // A bed remembers who claimed it by account id, so a character that changes id without
            // this arrives in its own base unable to sleep in its own bed. Done after the rename so
            // a refused rename leaves the world untouched. A Game Pass world is edited through an
            // unpacked working copy and the whole copy is packed back on SAVE, so patching the loose
            // world files here reaches the container the same way any other world edit does.
            if (hasOldIdentifier)
            {
                var worldFolder = workspace.WorldFolder;
                var claims = await Task.Run(
                    () => WorldSteamIdPatcher.PatchFolder(worldFolder, oldIdentifier, newIdentifier),
                    cancellationToken).ConfigureAwait(false);
                if (claims > 0)
                {
                    EditorLog.Info("PlayerSave",
                        $"Moved {claims} bed claim(s) from {oldIdentifier} to {newIdentifier}.");
                }
            }

            var saves = await DiscoverSavesAsync(workspace.WorldFolder, cancellationToken).ConfigureAwait(false);
            var renamed = saves.FirstOrDefault(save => string.Equals(save.Path, newPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The renamed player save was not rediscovered in this workspace.");
            var selection = await ReadSelectionAsync(renamed, cancellationToken).ConfigureAwait(false);
            TransferPlayerSession = selection.PlayerSession;
            Current = workspace with { Saves = saves, SelectedSave = renamed, Summary = selection.Summary, PlayerSession = selection.PlayerSession, WorldSession = null };
        }
        finally
        {
            BusyOperation = null; Changed?.Invoke(); _gate.Release();
        }
    }

    public async Task SaveSelectedAsync(CancellationToken cancellationToken = default)
    {
        var current = Current;
        var player = current?.PlayerSession;
        var world = current?.WorldSession;
        if (player is null && world is null) return;
        BusyOperation = "Writing save and backup…"; Changed?.Invoke();
        try
        {
            if (player is not null) await player.SaveAsync(cancellationToken); else if (world is not null) await world.SaveAsync(cancellationToken);

            // The editor wrote the .sav into the temp working copy; pack it straight back
            // into the Xbox container so SAVE means saved, exactly like the native app
            // (the wgs folder itself is backed up on the first write).
            if (current?.GamePass is { } gamePass)
            {
                BusyOperation = "Packing the save into the Game Pass container…"; Changed?.Invoke();
                try
                {
                    await Task.Run(() => gamePass.Set.ApplyWorld(gamePass.Container, gamePass.WorkingDir), cancellationToken).ConfigureAwait(false);
                }
                catch (GamePassUnsafeWriteException refused)
                {
                    // Nothing went wrong: the editor declined on purpose, because the save is in a
                    // state where Xbox would probably discard the edit. The working copy is still
                    // marked, because a player who closes the editor instead of resolving it would
                    // otherwise lose the edit to the startup sweep - but the caller shows this as a
                    // refusal to be answered, never as work that went missing.
                    ProtectWorkingDir(gamePass.WorkingDir);
                    EditorLog.Warn("GamePass",
                        $"Refused to pack into container '{gamePass.Container}': {refused.Message}");
                    throw;
                }
                catch
                {
                    // The edit reached the working copy but not the player's Xbox saves. That copy
                    // is now the only place it exists, and the next open (or the startup sweep)
                    // deletes working copies - so mark it to be kept, and let the failure reach the
                    // caller so the player is told rather than shown a successful-looking save.
                    ProtectWorkingDir(gamePass.WorkingDir);
                    EditorLog.Error("GamePass",
                        $"Packing into container '{gamePass.Container}' failed; the edited saves are kept "
                        + $"in {gamePass.WorkingDir}");
                    throw;
                }

                // The retry landed, so the working copy is no longer the only home for this work
                // and goes back to being an ordinary temp folder.
                ClearWorkingDirProtection(gamePass.WorkingDir);
                EditorLog.Info("GamePass", $"Saved into Game Pass container '{gamePass.Container}'.");
            }
        }
        finally { BusyOperation = null; Changed?.Invoke(); }
    }

    /// <summary>
    /// Everything known about whether an edit written into the open Game Pass save would survive,
    /// or null when the open save is not a Game Pass one. Never throws: a check that cannot be made
    /// must not be the thing that stops a screen from rendering.
    /// </summary>
    public GamePassWriteCheck? GamePassWriteState()
    {
        if (Current?.GamePass?.Set is not { } set) return null;
        try
        {
            return set.CheckWritable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            EditorLog.Warn("GamePass", $"Could not check whether this save is safe to write: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves again after the player has been shown why the editor refused and has said to do it
    /// anyway. The acceptance covers this open save only, so the next one starts from a refusal.
    /// </summary>
    /// <param name="reason">Who accepted the risk and why. Recorded in the log beside the write,
    /// because the damage this permits shows up days later with nothing on screen to explain it.</param>
    public async Task SaveSelectedAcceptingGamePassRiskAsync(
        string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Current?.GamePass?.Set is not { } set)
            throw new InvalidOperationException("No Game Pass save is open.");

        set.AllowUnsafeWrites(GamePassWriteOverride.AcceptRiskOfLosingThisSave(reason));
        await SaveSelectedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A Game Pass working copy holding edits that never reached the player's Xbox saves, because
    /// packing them back failed. It is exempt from the usual cleanup: deleting it would throw away
    /// the only copy of work the player believes they saved.
    /// </summary>
    private string? _protectedWorkingDir;

    /// <summary>
    /// The folder holding edits that could not be written back into the Xbox save, or null when
    /// there are none. The host shows this to the player so the work can be recovered by hand.
    /// </summary>
    public string? UnwrittenEditsFolder => _protectedWorkingDir;

    /// <summary>Marker file that exempts a working copy from cleanup.</summary>
    private const string UnwrittenMarkerFile = ".unwritten-edits";

    /// <summary>
    /// Marks a working copy as holding unwritten edits, in memory and on disk. The on-disk marker
    /// matters because the sweep that clears stale working copies runs in a later process: without
    /// it, closing the editor after a failed write and reopening it would delete the very folder
    /// the player was told to recover their work from.
    /// </summary>
    private void ProtectWorkingDir(string dir)
    {
        _protectedWorkingDir = dir;
        try
        {
            if (Directory.Exists(dir)) File.WriteAllText(Path.Combine(dir, UnwrittenMarkerFile), string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorLog.Warn("GamePass", $"Could not mark {dir} as holding unwritten edits: {ex.Message}");
        }
    }

    /// <summary>Lifts the exemption once the edits have actually reached the Xbox save.</summary>
    private void ClearWorkingDirProtection(string dir)
    {
        if (!string.Equals(dir, _protectedWorkingDir, StringComparison.OrdinalIgnoreCase)) return;
        _protectedWorkingDir = null;
        try
        {
            var marker = Path.Combine(dir, UnwrittenMarkerFile);
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover marker only costs a stale temp folder, never data.
            EditorLog.Warn("GamePass", $"Could not clear the unwritten-edits marker in {dir}: {ex.Message}");
        }
    }

    /// <summary>True when an open session is holding edits that have not been written yet.</summary>
    public bool HasStagedEdits => Current?.PlayerSession?.IsDirty == true || Current?.WorldSession?.IsDirty == true;

    /// <summary>
    /// Writes any staged edits out, so something that reads the saves back (the exporter) sees
    /// them. Only meaningful where writing does not touch the player's own files - on a folder
    /// opened read-only, where "writing" means updating the copy held in this tab.
    /// </summary>
    public async Task FlushStagedEditsAsync(CancellationToken cancellationToken = default)
    {
        if (_files.CanWrite)
        {
            throw new InvalidOperationException(
                "Refusing to flush staged edits to a writable folder: that would save the player's "
                + "files behind their back. Only a read-only workspace may be flushed.");
        }
        if (HasStagedEdits) await SaveSelectedAsync(cancellationToken).ConfigureAwait(false);
    }

    public void RevertSelected()
    {
        if (Current?.PlayerSession is { } player) player.Revert();
        else Current?.WorldSession?.Revert();
        Changed?.Invoke();
    }

    private async Task<WorkspaceSave[]> DiscoverSavesAsync(string worldFolder, CancellationToken cancellationToken)
    {
        var entries = await _files.ListSavesAsync(worldFolder, cancellationToken).ConfigureAwait(false);
        var discovered = new List<WorkspaceSave>(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Identifying each save reads and parses its header, and a world holds sixty-odd of
            // them. On a host where that read completes without ever waiting - a world already
            // unpacked into memory from a zip - the whole loop would run in one go and freeze the
            // page. Reporting progress yields, which both keeps the page alive and shows a count
            // that is actually moving.
            await ReportBusyAsync($"Reading saves… {discovered.Count + 1}/{entries.Count}").ConfigureAwait(false);
            discovered.Add(await CreateSaveAsync(entry, cancellationToken).ConfigureAwait(false));
        }

        return discovered
            .Where(save => save.Kind is SaveDocumentKind.Player or SaveDocumentKind.World or SaveDocumentKind.WorldMetadata)
            .OrderBy(save => SortOrder(save.Kind))
            .ThenBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<WorkspaceSave> CreateSaveAsync(SaveFileEntry entry, CancellationToken cancellationToken)
    {
        // A save whose header cannot be read is not fatal: the file name alone still classifies
        // it well enough to list, which is what the editor did before and how a save from a
        // newer game version stays visible instead of vanishing from the sidebar.
        string? saveClass = null;
        try
        {
            var header = await _files.ReadHeaderAsync(entry.Path, HeaderProbeBytes, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(header, writable: false);
            saveClass = SaveFolderScanner.ReadSaveClassFromHeader(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            EditorLog.Warn("Scan", $"Could not read the header of {entry.Name}: {ex.Message}");
        }

        return new WorkspaceSave(
            entry.Path,
            entry.RelativePath,
            entry.Name,
            entry.Length,
            Classify(saveClass, entry.Name),
            saveClass);
    }

    private static SaveDocumentKind Classify(string? saveClass, string fileName)
    {
        if (saveClass?.Contains("CharacterSave", StringComparison.OrdinalIgnoreCase) == true)
            return SaveDocumentKind.Player;
        if (saveClass?.Contains("WorldMetadataSave", StringComparison.OrdinalIgnoreCase) == true)
            return SaveDocumentKind.WorldMetadata;
        if (saveClass?.Contains("WorldSave", StringComparison.OrdinalIgnoreCase) == true)
            return SaveDocumentKind.World;

        // File names provide a useful fallback for a save whose custom class is newer
        // than this editor, while parsing remains restricted to known save categories.
        if (fileName.StartsWith("Player_", StringComparison.OrdinalIgnoreCase)) return SaveDocumentKind.Player;
        if (fileName.Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase)) return SaveDocumentKind.WorldMetadata;
        return fileName.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase)
            ? SaveDocumentKind.World
            : SaveDocumentKind.Unknown;
    }

    private async Task<SaveSelection> ReadSelectionAsync(WorkspaceSave save, CancellationToken cancellationToken)
    {
        if (save.Kind is not (SaveDocumentKind.Player or SaveDocumentKind.World or SaveDocumentKind.WorldMetadata))
            throw new InvalidOperationException($"'{save.Name}' is not a supported player or world save.");

        var bytes = await _files.ReadAllBytesAsync(save.Path, cancellationToken).ConfigureAwait(false);

        // Parsing a region save is the slow part (the Facility save is ~16 MB), so it stays off
        // the caller's thread exactly as it did when the reader opened the file itself.
        return await Task.Run(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            if (save.Kind == SaveDocumentKind.Player)
            {
                var data = PlayerSaveReader.ReadFromStream(stream);
                _recipeVocabulary.TryGetRecipes(out var recipes);
                _progressionVocabulary.TryGet(out var items, out var maps);
                _codexVocabulary.TryGet(out var codex);
                _itemUpgradeVocabulary.TryGet(out var upgrades);
                return new SaveSelection(PlayerSummary(save, data),
                    new PlayerSaveSession(data, save.Path, recipes, items, maps, codex, upgrades,
                        _language is null ? null : _language.Resource, _files), null);
            }

            var world = WorldSaveReader.ReadFromStream(stream);
            return new SaveSelection(WorldSummary(save, world), null, new WorldSaveSession(world, save.Path, _files));
        }, cancellationToken).ConfigureAwait(false);
    }

    private static PlayerSaveSummary PlayerSummary(WorkspaceSave save, PlayerSaveData data)
    {
        var steamId = PlayerSaveIdentity.GetSaveIdentifier(data.Raw)
            ?? (PlayerIdentifier.TryParseFromPlayerFileName(save.Path, out var parsed) ? parsed : null);
        return new PlayerSaveSummary(save, data.SaveClassName, data.AbfVersion, steamId, data.Phd,
            data.Stats.Money, data.Skills.Count, data.Skills.Count(skill => skill.Xp > 0),
            data.Traits.Count, data.Recipes.Count, data.Inventory.Main.Count);
    }

    private static WorldSaveSummary WorldSummary(WorkspaceSave save, WorldSaveData data)
        => new(save, data.SaveClassName, data.AbfVersion, data.Flags.Count, data.Deployables.Count,
            data.Containers.Count, data.Doors.Count, data.DroppedItems.Count, data.Npcs.Count,
            data.StoryProgressionRow, data.MinutesPassed);

    private static int SortOrder(SaveDocumentKind kind) => kind switch
    {
        SaveDocumentKind.WorldMetadata => 0,
        SaveDocumentKind.Player => 1,
        SaveDocumentKind.World => 2,
        _ => 3,
    };

    private static SavePlatform InferPlatform(string worldFolder, IReadOnlyList<WorkspaceSave> saves)
    {
        var normalized = worldFolder.Replace('\\', '/');
        if (normalized.Contains("/Packages/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/SystemAppData/wgs/", StringComparison.OrdinalIgnoreCase))
            return SavePlatform.GamePass;

        if (normalized.Contains("/compatdata/", StringComparison.OrdinalIgnoreCase)
            || saves.Where(save => save.Kind == SaveDocumentKind.Player)
                .Any(save => PlayerIdentifier.TryParseFromPlayerFileName(save.Path, out var id)
                    && PlayerIdentifier.IsSteamId(id))
            || PlayerIdentifier.IsSteamId(AccountSegment(worldFolder)))
            return SavePlatform.Steam;

        return SavePlatform.Unknown;
    }

    private static string? AccountSegment(string folder)
    {
        var parts = folder.Split('/', '\\');
        var index = Array.FindIndex(parts, part => part.Equals("SaveGames", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : null;
    }

    /// <summary>Best-effort removal of a Game Pass temp working copy that is no longer open.</summary>
    private void DeleteWorkingDir(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (string.Equals(dir, _protectedWorkingDir, StringComparison.OrdinalIgnoreCase))
        {
            EditorLog.Warn("GamePass",
                $"Keeping {dir}: it holds edits that could not be written back to the Xbox save.");
            return;
        }
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it for the next startup sweep.
        }
    }

    /// <summary>
    /// Removes leftover Game Pass working copies from prior runs (a crash can strand them in
    /// the temp folder). Called once at startup, before any container is extracted.
    /// </summary>
    public static void CleanupGamePassWorkingDirs()
    {
        var root = Path.Combine(Path.GetTempPath(), "AbioticEditor", "GamePass");
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                // A copy marked as holding unwritten edits is the only surviving version of work a
                // previous run failed to write back, so it outlives the run that made it.
                if (File.Exists(Path.Combine(dir, UnwrittenMarkerFile)))
                {
                    EditorLog.Warn("GamePass",
                        $"Keeping {dir}: it holds edits from an earlier run that never reached the Xbox save.");
                    continue;
                }
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stale copy that cannot be removed costs temp space and nothing else.
            }
        }
    }

    public void Dispose()
    {
        DeleteWorkingDir(Current?.GamePass?.WorkingDir);
        _gate.Dispose();
    }
}

public enum SaveDocumentKind { Unknown, Player, World, WorldMetadata }

public sealed record SaveWorkspace(
    string WorldFolder,
    IReadOnlyList<WorkspaceSave> Saves,
    WorkspaceSave? SelectedSave,
    SaveSummary? Summary,
    PlayerSaveSession? PlayerSession,
    WorldSaveSession? WorldSession,
    SavePlatform Platform,
    DiscoveredWorldSource? Source)
{
    /// <summary>
    /// The Xbox container session backing this workspace when it was opened from a Game Pass
    /// wgs folder. <see cref="WorldFolder"/> is then a temp extraction; the path shown to the
    /// player should be <see cref="GamePassWorkspaceSession.WgsFolder"/>.
    /// </summary>
    public GamePassWorkspaceSession? GamePass { get; init; }

    /// <summary>The folder to present to the player (the wgs folder for Game Pass workspaces).</summary>
    public string DisplayFolder => GamePass?.WgsFolder ?? WorldFolder;
}

/// <summary>An open Game Pass container behind a workspace: the wgs set, which container is
/// loaded, and the temp working copy the folder editor reads and writes.</summary>
public sealed record GamePassWorkspaceSession(
    GamePassSaveSet Set,
    string Container,
    string WorldName,
    string WgsFolder,
    string WorkingDir);

public sealed record WorkspaceSave(
    string Path,
    string RelativePath,
    string Name,
    long Length,
    SaveDocumentKind Kind,
    string? SaveClass)
{
    public string Size => Length switch
    {
        < 1024 => $"{Length} B",
        < 1024 * 1024 => $"{Length / 1024d:0.0} KB",
        _ => $"{Length / 1024d / 1024d:0.0} MB",
    };
}

public abstract record SaveSummary(WorkspaceSave Save, string? SaveClass, int? AbfVersion);

public sealed record PlayerSaveSummary(
    WorkspaceSave Save, string? SaveClass, int? AbfVersion, string? SteamId, string? Phd,
    int Money, int SkillCount, int SkillsWithXp, int TraitCount, int RecipeCount, int InventorySlotCount)
    : SaveSummary(Save, SaveClass, AbfVersion);

public sealed record WorldSaveSummary(
    WorkspaceSave Save, string? SaveClass, int? AbfVersion, int FlagCount, int DeployableCount,
    int ContainerCount, int DoorCount, int DroppedItemCount, int NpcCount, string? StoryChapter,
    int? MinutesPassed)
    : SaveSummary(Save, SaveClass, AbfVersion);

internal sealed record SaveSelection(SaveSummary Summary, PlayerSaveSession? PlayerSession, WorldSaveSession? WorldSession);
