using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Plugins;

namespace AbioticEditor.Web.Services;

/// <summary>Portable settings and diagnostics for the Razor host.</summary>
public sealed class HostSettingsService
{
    private static readonly Action<ILogger, string, Exception?> LogSettingsActionFailure = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(1003, "SettingsActionFailed"),
        "Settings action failed: {Action}");

    private readonly object _sync = new();
    private readonly RecipeVocabularyService _recipes;
    private readonly ProgressionVocabularyService _progression;
    private readonly CodexVocabularyService _codex;
    private readonly InventoryDismantleService _dismantle;
    private readonly HostLanguageService _languages;
    private readonly ILogger<HostSettingsService> _logger;
    private bool _pluginsLoaded;

    public HostSettingsService(RecipeVocabularyService recipes, ProgressionVocabularyService progression, CodexVocabularyService codex,
        InventoryDismantleService dismantle, HostLanguageService languages, ILogger<HostSettingsService> logger)
        => (_recipes, _progression, _codex, _dismantle, _languages, _logger) = (recipes, progression, codex, dismantle, languages, logger);

    /// <summary>
    /// Whether the installed game's tables read back with the mappings in use. Stays
    /// <see cref="GameDataHealthStatus.Healthy"/> until a check says otherwise, so the notice never
    /// flashes up before the first answer.
    /// </summary>
    public GameDataHealthStatus GameDataHealth { get; private set; }

    /// <summary>Raised on the thread that finished a game-data check, whenever <see cref="GameDataHealth"/> changes.</summary>
    public event Action? GameDataHealthChanged;

    private int _healthCheckStarted;

    private int _gameDataTabRequested;

    /// <summary>Asks the next Settings sheet to open on Game data (used by the out of date notice).</summary>
    public void RequestGameDataSettingsTab() => Volatile.Write(ref _gameDataTabRequested, 1);

    /// <summary>True once per request; reading it clears it so later opens start on General again.</summary>
    public bool ConsumeGameDataSettingsTabRequest() => Interlocked.Exchange(ref _gameDataTabRequested, 0) == 1;

    /// <summary>Runs the first game-data check in the background; later calls do nothing. Reloads re-check on their own.</summary>
    public void EnsureGameDataHealthChecked()
    {
        if (Interlocked.Exchange(ref _healthCheckStarted, 1) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                var provider = GameDataGate.CreateProvider(_languages.EffectiveGameDataLanguage);
                if (provider is not null) SetGameDataHealth(Core.Assets.GameDataHealth.Check(provider));
            }
            catch (Exception exception)
            {
                LogSettingsActionFailure(_logger, "Check game data", exception);
            }
        });
    }

    private void SetGameDataHealth(GameDataHealthStatus status)
    {
        // An unreadable check says nothing, so it must not clear a notice that an earlier check raised.
        if (status == GameDataHealthStatus.Unreadable) return;
        if (GameDataHealth == status) return;
        GameDataHealth = status;
        GameDataHealthChanged?.Invoke();
    }

    public void EnsurePluginsLoaded()
    {
        lock (_sync)
        {
            if (_pluginsLoaded) return;
            PluginManager.Shared.EnsureLoaded("blazor");
            _pluginsLoaded = true;
        }
    }

    public IReadOnlyList<PluginDescriptor> Plugins { get { EnsurePluginsLoaded(); return PluginManager.Shared.Descriptors; } }
    public IReadOnlyList<PluginCapability<AbioticEditor.Plugins.Ui.IWebTool>> WebTools { get { EnsurePluginsLoaded(); return PluginManager.Shared.WebTools; } }
    public string? SavedGamePath => GamePathStore.Saved;
    public string? PaksPath => AfInstallLocator.FindPaksDirectory();
    public string? MappingsPath => GameAssetProvider.FindConventionalMappings();
    public GameDataRegistry? BundledRegistry => GameDataRegistry.LoadBundled();
    public IReadOnlyList<AfInstallLocator.InstalledMod> InstalledMods => AfInstallLocator.FindAvailableMods(PaksPath);
    public bool ModsEnabled => ModLoadStore.ModsEnabled;
    public bool ModsLockedOff => ModLoadStore.DisabledByEnv;
    public bool DiagnosticLoggingEnabled
    {
        get => EditorLog.Enabled;
        set
        {
            EditorLog.Enabled = value;
            HostDiagnosticsStore.Save(value);
        }
    }
    public string LogDirectory => EditorLog.LogDirectory;
    public string CurrentLogPath => EditorLog.CurrentLogFilePath;
    public bool SaveGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { GamePathStore.Clear(); return true; }
        if (AfInstallLocator.ResolvePaksDirectory(path) is null)
            return false;
        GamePathStore.Save(path);
        return true;
    }

    public void ClearGamePath() => GamePathStore.Clear();
    public GameDataReloadStatus InstallMappings(string sourcePath)
    {
        try
        {
            var installed = GameAssetProvider.InstallUserMappings(sourcePath);
            var status = ReloadGameData();
            // An imported file that still cannot read the game must say so, not "installed".
            return status.CatalogsAvailable
                ? status with { Outcome = GameDataReloadOutcome.MappingsInstalled, Path = installed }
                : status;
        }
        catch (Exception exception)
        {
            LogSettingsActionFailure(_logger, "Import mappings", exception);
            return new(false, GameDataReloadOutcome.ImportFailed);
        }
    }

    public GameDataReloadStatus ReloadGameData()
    {
        try
        {
            GameDataGate.Invalidate();
            var provider = GameDataGate.CreateProvider(_languages.EffectiveGameDataLanguage);
            _recipes.Reload(); _progression.Reload(); _codex.Reload(); _dismantle.Reload();
            if (provider is null)
            {
                SetGameDataHealth(GameDataHealthStatus.Healthy);
                return new(false, GameDataReloadOutcome.NoInstall);
            }
            if (!provider.HasMappings)
            {
                SetGameDataHealth(GameDataHealthStatus.Healthy);
                return new(false, GameDataReloadOutcome.MissingMappings, PaksPath);
            }
            var health = Core.Assets.GameDataHealth.Check(provider);
            SetGameDataHealth(health);
            return health == GameDataHealthStatus.MappingsOutdated
                ? new(false, GameDataReloadOutcome.MappingsOutdated, PaksPath)
                : new(true, GameDataReloadOutcome.Reloaded, PaksPath);
        }
        catch (Exception exception)
        {
            LogSettingsActionFailure(_logger, "Reload game data", exception);
            return new(false, GameDataReloadOutcome.ReloadFailed);
        }
    }
    public void SetModsEnabled(bool enabled)
    {
        ModLoadStore.SetPersistedEnabled(enabled);
        GameDataGate.Invalidate();
    }
    public bool IsModEnabled(string modName) => ModLoadStore.IsModEnabled(modName);
    public void SetModEnabled(string modName, bool enabled)
    {
        ModLoadStore.SetModEnabled(modName, enabled);
        GameDataGate.Invalidate();
    }
    /// <summary>Subscribed mod.io mods the game has not finished downloading (empty in the browser build).</summary>
    public IReadOnlyList<ModIoInstallLocator.PendingMod> PendingMods
        => OperatingSystem.IsBrowser() ? [] : ModIoInstallLocator.FindPending();

    /// <summary>
    /// How a world's mod list compares with what the editor loads. Works from the on/off choices
    /// rather than a mounted provider, so asking never mounts the game files.
    /// </summary>
    public WorldModUsage WorldMods(IReadOnlyList<long> worldModIds)
    {
        if (OperatingSystem.IsBrowser()) return WorldModUsage.For(worldModIds, [], [], []);
        var installed = ModIoInstallLocator.FindInstalled();
        var loaded = ModsEnabled ? installed.Where(mod => IsModEnabled(mod.Name)).Select(mod => mod.Name).ToArray() : [];
        return WorldModUsage.For(worldModIds, loaded, installed, ModIoInstallLocator.FindPending());
    }

    /// <summary>
    /// Switches on exactly the installed mod.io mods this world uses (others off) and reloads game
    /// data. Mods from the game's <c>~mods</c>/<c>LogicMods</c> folders are left as they are.
    /// </summary>
    public GameDataReloadStatus UseWorldMods(IReadOnlyList<long> worldModIds)
    {
        var used = worldModIds.ToHashSet();
        foreach (var mod in ModIoInstallLocator.FindInstalled())
            if (mod.ModIoId is { } id) ModLoadStore.SetModEnabled(mod.Name, used.Contains(id));
        if (!ModLoadStore.PersistedEnabled && used.Count > 0) ModLoadStore.SetPersistedEnabled(true);
        return ReloadGameData();
    }

    public bool SetPluginEnabled(PluginDescriptor plugin, bool enabled) => plugin.SetEnabled(enabled);
}

public sealed record GameDataReloadStatus(bool CatalogsAvailable, GameDataReloadOutcome Outcome, string? Path = null);

public enum GameDataReloadOutcome { Reloaded, MissingMappings, NoInstall, MappingsInstalled, ImportFailed, ReloadFailed, MappingsOutdated }
