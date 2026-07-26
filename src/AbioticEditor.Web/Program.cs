using AbioticEditor.Web.Components;
using AbioticEditor.Web.Services;
using System.Text;

namespace AbioticEditor.Web;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var localHostListening = LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "LocalHostListening"),
            "Abiotic Editor local host listening on {LocalUrl}");

        // Desktop launchers and shortcuts are free to choose an arbitrary working
        // directory. Resolve wwwroot, templates, mappings, and notices beside the
        // published executable instead of against Environment.CurrentDirectory.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        // The published editor is a local desktop companion, not a network service. Do not
        // replace this with a wildcard binding: selected save paths and plugin tools are local.
        var localUrl = LocalHostEndpoint.Resolve(Environment.GetEnvironmentVariable("ABIOTIC_EDITOR_URL"));
        builder.WebHost.UseUrls(localUrl);
        // Build output has no physical wwwroot (the Web SDK only materializes it on publish),
        // so a plain `dotnet run` - which hosts in Production - would serve no CSS/JS at all.
        // The static-web-assets manifest in the build output maps requests back to the source
        // wwwroot; loading it is a no-op on published layouts, where the manifest is absent.
        builder.WebHost.UseStaticWebAssets();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddSingleton<SaveLibraryService>();
        builder.Services.AddSingleton<RecipeVocabularyService>();
        builder.Services.AddSingleton<ItemUpgradeVocabularyService>();
        builder.Services.AddSingleton<InventoryDismantleService>();
        builder.Services.AddSingleton<ProgressionVocabularyService>();
        builder.Services.AddSingleton<CodexVocabularyService>();
        builder.Services.AddSingleton<TraderVocabularyService>();
        builder.Services.AddSingleton<SaveWorkspaceSessionService>();
        builder.Services.AddSingleton<RecipeProgressGateService>();
        builder.Services.AddSingleton<SiblingWorldBedService>();
        builder.Services.AddSingleton<CreateWorldService>();
        builder.Services.AddSingleton<IniEditorSessionService>();
        builder.Services.AddSingleton<HostSettingsService>();
        builder.Services.AddSingleton<HostLanguageService>();
        builder.Services.AddSingleton<HostThemeService>();
        builder.Services.AddSingleton<HostSpoilerPreferences>();
        builder.Services.AddSingleton<ShellPreferencesService>();
        builder.Services.AddSingleton<InventorySelectionService>();
        builder.Services.AddSingleton<SlotDragDropService>();
        builder.Services.AddSingleton<ItemCatalogService>();
        builder.Services.AddSingleton<GameArtService>();
        builder.Services.AddSingleton<CustomizationCatalogService>();
        builder.Services.AddSingleton<SteamStatusService>();
        builder.Services.AddSingleton<SaveSemanticDiff>();
        builder.Services.AddSingleton<SaveComparisonService>();
#if !NEXUSMODS
        // Absent from the Nexus Mods build: that channel manages its own file versions and its
        // guidelines discourage bundled auto-updaters, so no update code ships there at all.
        builder.Services.AddSingleton<HostUpdateService>();
#endif
        builder.Services.AddSingleton<UserFacingErrorService>();
        builder.Services.AddSingleton<WebToolHostService>();
        builder.Services.AddScoped<BrowserSaveImportService>();
        // Self-hosted Razor builds use the local OS for pickers, file reveal, and links.
        // Browser-only deployments retain manual path entry because a remote server cannot
        // access a visitor's file system.
        builder.Services.AddSingleton<DesktopHostService>();
        builder.Services.AddSingleton<DesktopWindowHost>();
        builder.Services.AddSingleton<AbioticEditor.Ui.IFilePicker>(provider => provider.GetRequiredService<DesktopHostService>());
        builder.Services.AddSingleton<AbioticEditor.Ui.IFolderPicker>(provider => provider.GetRequiredService<DesktopHostService>());
        builder.Services.AddSingleton<AbioticEditor.Ui.IExternalNavigationService>(provider => provider.GetRequiredService<DesktopHostService>());
        builder.Services.AddScoped<ModalService>();
        builder.Services.AddScoped<ToastService>();
        var app = builder.Build();
        localHostListening(app.Logger, localUrl, null);
        // A crash can strand Game Pass temp extractions; sweep them before any new open.
        try { SaveWorkspaceSessionService.CleanupGamePassWorkingDirs(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // Serve the built-in styles and scripts from the build manifest rather than by plain
        // file path. The per-screen stylesheets live in one generated bundle that a rebuild
        // deletes and rewrites in place, so with plain paths a window open at that moment asked
        // for a file that briefly did not exist, got nothing, and stayed completely unstyled
        // until it was reloaded by hand. Manifest URLs carry a content stamp, so a rebuild
        // produces a new address instead of breaking the old one.
        app.MapStaticAssets(StaticAssetManifest.ResolvePath());
        app.UseAntiforgery();
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/item-icons/{itemId}", async (string itemId, ItemCatalogService catalog, HttpResponse response) =>
        {
            var path = await catalog.GetIconPathAsync(itemId);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            response.Headers.CacheControl = "public,max-age=86400,immutable";
            return Results.File(path, "image/png", enableRangeProcessing: false);
        });
        app.MapGet("/game-art/{*gameRef}", async (string gameRef, GameArtService art, HttpResponse response) =>
        {
            var path = await art.GetTexturePathAsync(Uri.UnescapeDataString(gameRef));
            if (path is null || !File.Exists(path)) return Results.NotFound();
            response.Headers.CacheControl = "public,max-age=86400,immutable";
            return Results.File(path, "image/png", enableRangeProcessing: false);
        });
        app.MapGet("/wiki-image/{*fileName}", async (string fileName, GameArtService art, HttpResponse response) =>
        {
            var path = await art.GetWikiImagePathAsync(Uri.UnescapeDataString(fileName));
            if (path is null || !File.Exists(path)) return Results.NotFound();
            response.Headers.CacheControl = "public,max-age=86400,immutable";
            // Wiki files can come back as png/jpg/webp/gif (WikiImageCache validates by magic
            // bytes, not by the requested name), so serve the content type the cached extension
            // actually reflects rather than assuming png like the pak-extracted art above.
            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "image/png",
            };
            return Results.File(path, contentType, enableRangeProcessing: false);
        });
        app.MapGet("/plugin-tools/{key}/content/{**asset}", (string key, string? asset, WebToolHostService tools) =>
        {
            if (!tools.TryGet(key, out var session) || session is null) return Results.NotFound();
            if (session.Content.Html is { } html && string.IsNullOrWhiteSpace(asset))
                return Results.Content(WebToolHostService.InjectBridge(html, key), "text/html; charset=utf-8");
            if (!WebToolHostService.TryResolveAsset(session, asset, out var path) || path is null) return Results.NotFound();
            if (Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase))
                return Results.Content(WebToolHostService.InjectBridge(File.ReadAllText(path), key), "text/html; charset=utf-8");
            return Results.File(path);
        });
        app.MapPost("/plugin-tools/{key}/request", async (string key, HttpRequest request, WebToolHostService tools, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var reply = await tools.RequestAsync(key, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
            return Results.Text(reply ?? string.Empty, "application/json; charset=utf-8");
        });
        app.MapPost("/plugin-tools/{key}/log", async (string key, HttpRequest request, WebToolHostService tools, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            tools.Log(key, await reader.ReadToEndAsync(cancellationToken));
            return Results.NoContent();
        });
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // Keep the native window on this entry thread. On Windows, Photino/WebView2
        // requires the STA apartment established by Main's attribute; awaiting startup
        // here could resume on a thread-pool MTA thread.
        app.StartAsync().GetAwaiter().GetResult();
        var desktopWindow = app.Services.GetRequiredService<DesktopWindowHost>();
        if (desktopWindow.IsEnabled())
        {
            DesktopConsole.HideOwnConsoleWindow();
            try
            {
                desktopWindow.Run(localUrl);
            }
            finally
            {
                app.StopAsync().GetAwaiter().GetResult();
            }
        }
        else
        {
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
        }
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
