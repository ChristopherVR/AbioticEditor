using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
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
builder.Services.AddScoped<SiblingWorldBedService>();
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

var host = builder.Build();
await StageBundledGameDataAsync(host.Services);
await host.RunAsync();

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
/// The language comes from the browser, which is what Blazor sets the current culture from. Tried
/// most specific first ("pt-BR" before "pt"), because the game ships some regional variants and
/// not their base language.
/// </remarks>
static async Task<GameDataRegistry?> FetchRegistryAsync(HttpClient http)
{
    var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
    var candidates = new List<string?>();
    if (!string.IsNullOrEmpty(uiCulture.Name)) candidates.Add(uiCulture.Name);
    if (!string.IsNullOrEmpty(uiCulture.TwoLetterISOLanguageName)) candidates.Add(uiCulture.TwoLetterISOLanguageName);
    candidates.Add(null); // the default dump, which always ships

    foreach (var culture in candidates)
    {
        try
        {
            var response = await http.GetAsync($"registry/{GameDataRegistry.FileNameFor(culture)}").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) continue;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (GameDataRegistry.TryRead(bytes) is { } registry) return registry;
        }
        catch (HttpRequestException)
        {
            // That language did not ship; try the next candidate.
        }
    }
    return null;
}
