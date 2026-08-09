using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using AbioticEditor.Core.Assets;
using AbioticEditor.Web.Services;
using AbioticEditor.Web.Wasm;
using AbioticEditor.Web.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Everything below runs inside one browser tab, so Scoped and Singleton amount to the same
// lifetime. Scoped is used throughout because several of these reach JavaScript, and that is
// the lifetime the Blazor WebAssembly templates register JS-dependent services with.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// --- the seams the shared screens reach the outside world through ---
builder.Services.AddScoped<BrowserSaveFileSystem>();
builder.Services.AddScoped<ISaveFileSystem>(sp => sp.GetRequiredService<BrowserSaveFileSystem>());
builder.Services.AddScoped<ISaveTemplateSource, BrowserSaveTemplateSource>();
builder.Services.AddScoped<IDiagnosticsLogDelivery, BrowserLogDelivery>();
builder.Services.AddScoped<ISaveExporter, BrowserSaveExporter>();
builder.Services.AddScoped<SaveExportService>();
builder.Services.AddScoped<BrowserFilePickerService>();
builder.Services.AddScoped<AbioticEditor.Ui.IFilePicker>(sp => sp.GetRequiredService<BrowserFilePickerService>());
builder.Services.AddScoped<AbioticEditor.Ui.IFolderPicker>(sp => sp.GetRequiredService<BrowserFilePickerService>());
builder.Services.AddScoped<AbioticEditor.Ui.IExternalNavigationService, BrowserNavigationService>();

// --- the shared editor services, mirroring the desktop host ---
// Absent on purpose, because they cannot work in a browser: WebToolHostService and the plugin
// host (loads assemblies), HostUpdateService (replaces an executable), and the desktop
// pickers/window. SaveLibraryService IS registered - a shared screen injects it - but it
// reports CanDiscover == false here and returns nothing rather than scanning a virtual disk.
builder.Services.AddScoped<SaveLibraryService>();
builder.Services.AddScoped<RecipeVocabularyService>();
builder.Services.AddScoped<ItemUpgradeVocabularyService>();
builder.Services.AddScoped<InventoryDismantleService>();
builder.Services.AddScoped<ProgressionVocabularyService>();
builder.Services.AddScoped<CodexVocabularyService>();
builder.Services.AddScoped<TraderVocabularyService>();
builder.Services.AddScoped<SaveWorkspaceSessionService>();
builder.Services.AddScoped<RecipeProgressGateService>();
// Reading a world costs seconds here, so what it produced is kept between visits.
builder.Services.AddScoped<IWorldFactsCache, BrowserWorldFactsCache>();
builder.Services.AddScoped<SiblingWorldBedService>();
builder.Services.AddScoped<WorldLevelIndexService>();
builder.Services.AddScoped<StoryFlagSyncService>();
builder.Services.AddScoped<CreateWorldService>();
builder.Services.AddScoped<IniEditorSessionService>();
builder.Services.AddScoped<HostSettingsService>();
builder.Services.AddScoped<HostLanguageService>();
builder.Services.AddScoped<HostThemeService>();
builder.Services.AddScoped<HostSpoilerPreferences>();
builder.Services.AddScoped<HostAdvancedPreferences>();
builder.Services.AddScoped<ShellPreferencesService>();
builder.Services.AddScoped<InventorySelectionService>();
builder.Services.AddScoped<SlotDragDropService>();
builder.Services.AddScoped<ItemCatalogService>();
builder.Services.AddScoped<GameArtService>();
builder.Services.AddScoped<CustomizationCatalogService>();
builder.Services.AddScoped<SteamStatusService>();
builder.Services.AddScoped<SaveSemanticDiff>();
builder.Services.AddScoped<SaveComparisonService>();
builder.Services.AddScoped<UserFacingErrorService>();
builder.Services.AddScoped<ModalService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<UnsavedChangesGuard>();

var host = builder.Build();
UseBrowserStorageForPreferences(host.Services);
await StageBundledGameDataAsync(host.Services);
await host.RunAsync();

/// <summary>
/// Keeps the editor's own settings in the browser's storage instead of in files.
/// </summary>
/// <remarks>
/// A WebAssembly app does have a file system, and it is thrown away the moment the tab reloads -
/// so the desktop's one-line settings files silently lost every choice the player made. Display
/// language made that obvious, because choosing one reloads the page and the editor came straight
/// back in the language it started in, but the theme and the game-data language were just as
/// affected. localStorage is the browser's equivalent and survives properly.
///
/// Installed before any screen renders, and before the game data is fetched - that fetch reads
/// the saved game-data language to decide which translation dump to download.
/// </remarks>
static void UseBrowserStorageForPreferences(IServiceProvider services)
{
    // In-process interop: these are read while services are being constructed, which cannot wait
    // on an async call. Only WebAssembly offers it, which is exactly where this runs.
    if (services.GetRequiredService<IJSRuntime>() is not IJSInProcessRuntime js) return;

    string? Read(string key)
    {
        try { return js.Invoke<string?>("localStorage.getItem", key); }
        catch (JSException) { return null; }
    }

    void Write(string key, string? value)
    {
        // A browser with storage switched off (private mode, blocked cookies) throws here. The
        // setting still applies for this session; it just will not be remembered, which is far
        // better than the editor refusing to change a setting at all.
        try
        {
            if (value is null) js.InvokeVoid("localStorage.removeItem", key);
            else js.InvokeVoid("localStorage.setItem", key, value);
        }
        catch (JSException) { }
    }

    HostPreferenceStore.UseStore(Read, Write);
    GameDataLanguageStore.UseStore(
        () => Read(HostPreferenceStore.Keys.GameDataLanguage),
        value => Write(HostPreferenceStore.Keys.GameDataLanguage, value));
}

// Item names, recipes, the codex and the rest come from a dump of the game's data tables that
// ships with the editor, fetched once at startup and handed to Core directly.
//
// This is the whole reason the browser can show real names at all: the paks themselves cannot be
// mounted in a tab (docs/PROGRESS.md round-45 has the measurements), but one dump is ~2 MB.
// Failing to fetch it is not fatal - the editor falls back to internal ids, which is the same
// thing the desktop app does when it cannot find the game.
static async Task StageBundledGameDataAsync(IServiceProvider services)
{
    try
    {
        var http = services.GetRequiredService<HttpClient>();
        var registry = await FetchRegistryAsync(http).ConfigureAwait(false);
        GameDataRegistry.Supply(registry);

        // Console, not just the log file: if this ever fails the whole editor comes up looking
        // fine but with no item names, no recipes and no pictures anywhere, and the log it would
        // otherwise complain to is a file nobody can reach from a browser.
        if (registry is null)
        {
            Console.Error.WriteLine(
                "Abiotic Editor: the bundled game data could not be read; names will show as internal ids.");
        }

        // The list of pictures that shipped. Failing this is much less serious than the registry:
        // every screen that draws one already has a fallback symbol for when the picture is
        // missing, so the editor just looks plainer.
        try
        {
            var manifest = await http.GetByteArrayAsync($"art/{BundledArt.ManifestFileName}").ConfigureAwait(false);
            BundledArt.Supply(BundledArt.TryRead(manifest));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Abiotic Editor: could not load the bundled pictures. {exception.Message}");
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Abiotic Editor: could not load the bundled game data. {exception.Message}");
        AbioticEditor.Core.Diagnostics.EditorLog.Warn(
            "Assets", $"Could not load the bundled game data; names will show as internal ids. {exception.Message}");
    }
}

/// <summary>
/// Fetches the game-data dump for the player's language, falling back to the game's default.
/// </summary>
/// <remarks>
/// The dumps are per language because every display name, description, email and journal entry in
/// them is translated text: one file would mean a German player reading English item names. Only
/// the matching file is downloaded, so carrying ten costs the player nothing.
///
/// The language is the one the player chose for game names if they chose one, and otherwise the
/// browser's, which is what Blazor sets the current culture from. Tried most specific first
/// ("pt-BR" before "pt"), because the game ships some regional variants and not their base
/// language.
/// </remarks>
static async Task<GameDataRegistry?> FetchRegistryAsync(HttpClient http)
{
    // Resolved against the list of what actually shipped, so exactly one file is requested and a
    // player whose language is not covered costs no failed request at all.
    //
    // The chosen game-data language wins over the browser's: the game ships languages the editor
    // itself is not translated into, and someone playing in Japanese wants Japanese item names
    // whichever language the buttons are in. Without this the setting existed and did nothing.
    var preferred = GameDataLanguageStore.Saved ?? System.Globalization.CultureInfo.CurrentUICulture.Name;
    var culture = GameDataRegistry.BestCultureFor(preferred);

    foreach (var candidate in new[] { culture, null })
    {
        try
        {
            var response = await http.GetAsync($"registry/{GameDataRegistry.FileNameFor(candidate)}").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) continue;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (GameDataRegistry.TryRead(bytes) is { } registry) return registry;
        }
        catch (HttpRequestException)
        {
            // Fall through to the default dump, which always ships.
        }

        if (candidate is null) break;
    }
    return null;
}
