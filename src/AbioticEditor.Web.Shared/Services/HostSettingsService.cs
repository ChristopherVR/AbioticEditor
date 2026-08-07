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
    public IReadOnlyList<AfInstallLocator.InstalledMod> InstalledMods => AfInstallLocator.FindMods(PaksPath);
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
            return status with { Outcome = GameDataReloadOutcome.MappingsInstalled, Path = installed };
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
            using var provider = GameDataGate.CreateProvider(_languages.EffectiveGameDataLanguage);
            _recipes.Reload(); _progression.Reload(); _codex.Reload(); _dismantle.Reload();
            return provider is null
                ? new(false, GameDataReloadOutcome.NoInstall)
                : new(provider.HasMappings, provider.HasMappings
                    ? GameDataReloadOutcome.Reloaded
                    : GameDataReloadOutcome.MissingMappings, PaksPath);
        }
        catch (Exception exception)
        {
            LogSettingsActionFailure(_logger, "Reload game data", exception);
            return new(false, GameDataReloadOutcome.ReloadFailed);
        }
    }
    public void SetModsEnabled(bool enabled) => ModLoadStore.SetPersistedEnabled(enabled);
    public bool IsModEnabled(string modName) => ModLoadStore.IsModEnabled(modName);
    public void SetModEnabled(string modName, bool enabled) => ModLoadStore.SetModEnabled(modName, enabled);
    public bool SetPluginEnabled(PluginDescriptor plugin, bool enabled) => plugin.SetEnabled(enabled);
}

public sealed record GameDataReloadStatus(bool CatalogsAvailable, GameDataReloadOutcome Outcome, string? Path = null);

public enum GameDataReloadOutcome { Reloaded, MissingMappings, NoInstall, MappingsInstalled, ImportFailed, ReloadFailed }
