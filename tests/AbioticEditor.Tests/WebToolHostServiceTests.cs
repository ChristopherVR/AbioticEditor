using System.Reflection;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers the Razor host boundary for trusted plugin web tools.  In particular, static assets
/// must remain inside the tool's declared directory, and the browser bridge must stay scoped
/// to the opaque per-open session key.
/// </summary>
public sealed class WebToolHostServiceTests
{
    [Fact]
    public async Task Open_request_and_close_keep_the_bridge_scoped_to_its_session()
    {
        using var pluginDirectory = new TempDirectory();
        var tool = new RecordingWebTool(WebToolContent.FromHtml("<html><head><title>Tool</title></head><body></body></html>"));
        var host = new RecordingPluginHost();
        var service = new WebToolHostService(CreateWorkspace());

        var session = service.Open(CreateCapability(pluginDirectory.Path, host, tool));

        Assert.Matches("^[a-f0-9]{32}$", session.Key);
        Assert.Same(host, tool.Context!.Host);
        Assert.Null(tool.Context.ActiveSavePath);
        Assert.Same(session, Assert.IsType<WebToolSession>(GetSession(service, session.Key)));

        var response = await service.RequestAsync(session.Key, "{\"action\":\"ping\"}", CancellationToken.None);
        Assert.Equal("{\"ok\":true}", response);
        Assert.Equal("{\"action\":\"ping\"}", tool.LastMessage);
        Assert.Same(session.Context, tool.LastRequestContext);

        service.Log(session.Key, "ready");
        Assert.Contains("[web] ready", host.LogMessages);

        var injected = WebToolHostService.InjectBridge(session.Content.Html!, session.Key);
        Assert.Contains($"/plugin-tools/{session.Key}", injected, StringComparison.Ordinal);
        Assert.True(injected.IndexOf("<script>", StringComparison.Ordinal) < injected.IndexOf("</head>", StringComparison.OrdinalIgnoreCase));

        service.Close(session.Key);
        Assert.False(service.TryGet(session.Key, out _));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RequestAsync(session.Key, "{}", CancellationToken.None));
    }

    [Fact]
    public void Asset_resolution_allows_only_existing_files_under_the_declared_root()
    {
        using var pluginDirectory = new TempDirectory();
        var assets = Path.Combine(pluginDirectory.Path, "dist");
        Directory.CreateDirectory(Path.Combine(assets, "scripts"));
        File.WriteAllText(Path.Combine(assets, "index.html"), "entry");
        File.WriteAllText(Path.Combine(assets, "scripts", "app.js"), "app");
        var outside = Path.Combine(pluginDirectory.Path, "private.txt");
        File.WriteAllText(outside, "private");

        var service = new WebToolHostService(CreateWorkspace());
        var session = service.Open(CreateCapability(pluginDirectory.Path, new RecordingPluginHost(),
            new RecordingWebTool(WebToolContent.FromDirectory("dist"))));

        Assert.True(WebToolHostService.TryResolveAsset(session, null, out var entry));
        Assert.Equal(Path.Combine(assets, "index.html"), entry);
        Assert.True(WebToolHostService.TryResolveAsset(session, "scripts/app.js", out var script));
        Assert.Equal(Path.Combine(assets, "scripts", "app.js"), script);

        Assert.False(WebToolHostService.TryResolveAsset(session, "../private.txt", out _));
        Assert.False(WebToolHostService.TryResolveAsset(session, "missing.js", out _));
    }

    private static WebToolSession? GetSession(WebToolHostService service, string key)
        => service.TryGet(key, out var session) ? session : null;

    private static SaveWorkspaceSessionService CreateWorkspace()
        => new(new RecipeVocabularyService(), new ProgressionVocabularyService(), new CodexVocabularyService(), new DesktopSaveFileSystem());

    private static PluginCapability<IWebTool> CreateCapability(string folder, IPluginHost host, IWebTool tool)
    {
        var manifest = new PluginManifest { Id = "tests.web-tool", Name = "Web tool test", Version = "1.0.0", EntryAssembly = "Test.dll" };
        var constructor = typeof(PluginDescriptor).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(PluginManifest), typeof(string), typeof(string)], null)!;
        var descriptor = (PluginDescriptor)constructor.Invoke([manifest, folder, Path.Combine(folder, "plugin.json")]);
        typeof(PluginDescriptor).GetProperty(nameof(PluginDescriptor.Host))!.SetValue(descriptor, host);
        return new PluginCapability<IWebTool>(descriptor, tool);
    }

    private sealed class RecordingWebTool(WebToolContent content) : IWebTool
    {
        public string Id => "test-tool";
        public string Title => "Test tool";
        public IWebToolContext? Context { get; private set; }
        public IWebToolContext? LastRequestContext { get; private set; }
        public string? LastMessage { get; private set; }

        public WebToolContent CreateContent(IWebToolContext context)
        {
            Context = context;
            return content;
        }

        public Task<string?> HandleMessageAsync(string message, IWebToolContext context, CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            LastRequestContext = context;
            return Task.FromResult<string?>("{\"ok\":true}");
        }
    }

    private sealed class RecordingPluginHost : IPluginHost
    {
        public List<string> LogMessages { get; } = [];
        public Version SdkVersion => new(1, 0);
        public Version HostVersion => new(1, 0);
        public string HostKind => "test";
        public IPluginLog Log => new RecordingLog(LogMessages);
        public IHostUi Ui { get; } = NullHostUi.Instance;
        public string DataDirectory => Path.GetTempPath();
    }

    private sealed class RecordingLog(List<string> messages) : IPluginLog
    {
        public void Info(string message) => messages.Add(message);
        public void Warn(string message) => messages.Add(message);
        public void Error(string message, Exception? exception = null) => messages.Add(message);
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("abiotic-web-tool-");
        public string Path => _directory.FullName;
        public void Dispose()
        {
            try { _directory.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
