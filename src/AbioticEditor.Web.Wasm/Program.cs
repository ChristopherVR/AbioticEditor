using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
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

await builder.Build().RunAsync();
