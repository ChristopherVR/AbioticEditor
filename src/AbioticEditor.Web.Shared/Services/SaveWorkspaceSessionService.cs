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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RecipeVocabularyService _recipeVocabulary;
    private readonly ItemUpgradeVocabularyService _itemUpgradeVocabulary;
    private readonly ProgressionVocabularyService _progressionVocabulary;
    private readonly CodexVocabularyService _codexVocabulary;
    private readonly HostLanguageService? _language;

    public SaveWorkspaceSessionService(RecipeVocabularyService recipeVocabulary, ProgressionVocabularyService progressionVocabulary, CodexVocabularyService codexVocabulary)
        : this(recipeVocabulary, new ItemUpgradeVocabularyService(), progressionVocabulary, codexVocabulary) { }

    public SaveWorkspaceSessionService(RecipeVocabularyService recipeVocabulary, ItemUpgradeVocabularyService itemUpgradeVocabulary, ProgressionVocabularyService progressionVocabulary, CodexVocabularyService codexVocabulary, HostLanguageService? language = null)
    {
        _recipeVocabulary = recipeVocabulary;
        _itemUpgradeVocabulary = itemUpgradeVocabulary;
        _progressionVocabulary = progressionVocabulary;
        _codexVocabulary = codexVocabulary;
        _language = language;
    }

    /// <summary>The currently open workspace, or <see langword="null"/> before a folder is opened.</summary>
    public SaveWorkspace? Current { get; private set; }
    /// <summary>Most recently opened player session in this workspace, retained for staged container transfers.</summary>
    public PlayerSaveSession? TransferPlayerSession { get; private set; }
    /// <summary>Most recently opened world session, retained for staged carried-pet placement.</summary>
    public WorldSaveSession? TransferWorldSession { get; private set; }
    public string? BusyOperation { get; private set; }
    public event Action? Changed;

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
            var (session, saves) = await Task.Run(() =>
            {
                var set = GamePassSaveSet.Open(wgsFolder);
                var working = Path.Combine(Path.GetTempPath(), "AbioticEditor", "GamePass",
                    $"{container}-{Guid.NewGuid():N}");
                var worldName = set.ExtractWorld(container, working);
                return (new GamePassWorkspaceSession(set, container, worldName, wgsFolder, working),
                    DiscoverSaves(working));
            }, cancellationToken).ConfigureAwait(false);

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

        var fullPath = Path.GetFullPath(worldFolder);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"The world save folder does not exist: {fullPath}");

        // A picked/dropped wgs container folder has no loose .sav files, so route it through
        // the Game Pass extract flow instead of opening an empty workspace.
        if (GamePassSaveSet.IsGamePassFolder(fullPath))
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
            BusyOperation = "Scanning save folder…"; Changed?.Invoke();
            var previousWorkingDir = Current?.GamePass?.WorkingDir;
            var saves = await Task.Run(() => DiscoverSaves(fullPath), cancellationToken).ConfigureAwait(false);
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
            var fullPath = Path.GetFullPath(savePath);
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
            var selection = await Task.Run(() => ReadSelection(save), cancellationToken).ConfigureAwait(false);
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
    /// embedded SaveIdentifier and preserves the original as a backup.
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
            var newPath = await Task.Run(() => PlayerSaveIdentity.ChangeSteamId(player.Path, newIdentifier), cancellationToken).ConfigureAwait(false);
            // A Game Pass world is edited through an unpacked copy, and the repack walks the
            // container's own list of names. Rename it there too or the new id never reaches the
            // container and the old player quietly comes back.
            if (workspace.GamePass is { } gamePassRename)
            {
                var newFileName = Path.GetFileName(newPath);
                await Task.Run(() => gamePassRename.Set.RenamePlayerSave(gamePassRename.Container, oldFileName, newFileName), cancellationToken)
                    .ConfigureAwait(false);
            }
            var saves = await Task.Run(() => DiscoverSaves(workspace.WorldFolder), cancellationToken).ConfigureAwait(false);
            var renamed = saves.FirstOrDefault(save => string.Equals(save.Path, newPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The renamed player save was not rediscovered in this workspace.");
            var selection = await Task.Run(() => ReadSelection(renamed), cancellationToken).ConfigureAwait(false);
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
                await Task.Run(() => gamePass.Set.ApplyWorld(gamePass.Container, gamePass.WorkingDir), cancellationToken).ConfigureAwait(false);
                EditorLog.Info("GamePass", $"Saved into Game Pass container '{gamePass.Container}'.");
            }
        }
        finally { BusyOperation = null; Changed?.Invoke(); }
    }

    public void RevertSelected()
    {
        if (Current?.PlayerSession is { } player) player.Revert();
        else Current?.WorldSession?.Revert();
        Changed?.Invoke();
    }

    private static WorkspaceSave[] DiscoverSaves(string worldFolder)
        => Directory.EnumerateFiles(worldFolder, "*.sav", SearchOption.AllDirectories)
            .Select(path => CreateSave(path, worldFolder))
            .Where(save => save.Kind is SaveDocumentKind.Player or SaveDocumentKind.World or SaveDocumentKind.WorldMetadata)
            .OrderBy(save => SortOrder(save.Kind))
            .ThenBy(save => save.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static WorkspaceSave CreateSave(string path, string worldFolder)
    {
        var info = new FileInfo(path);
        var saveClass = SaveFolderScanner.ReadSaveClassFromHeader(path);
        return new WorkspaceSave(
            info.FullName,
            Path.GetRelativePath(worldFolder, info.FullName),
            info.Name,
            info.Length,
            Classify(saveClass, info.Name),
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

    private SaveSelection ReadSelection(WorkspaceSave save)
    {
        if (save.Kind == SaveDocumentKind.Player)
        {
            var data = PlayerSaveReader.ReadFromFile(save.Path);
            _recipeVocabulary.TryGetRecipes(out var recipes);
            _progressionVocabulary.TryGet(out var items, out var maps);
            _codexVocabulary.TryGet(out var codex);
            _itemUpgradeVocabulary.TryGet(out var upgrades);
            return new SaveSelection(PlayerSummary(save, data),
                new PlayerSaveSession(data, save.Path, recipes, items, maps, codex, upgrades,
                    _language is null ? null : _language.Resource), null);
        }

        if (save.Kind is SaveDocumentKind.World or SaveDocumentKind.WorldMetadata)
        {
            var data = WorldSaveReader.ReadFromFile(save.Path);
            return new SaveSelection(WorldSummary(save, data), null, new WorldSaveSession(data, save.Path));
        }

        throw new InvalidOperationException($"'{save.Name}' is not a supported player or world save.");
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
    private static void DeleteWorkingDir(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
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
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
