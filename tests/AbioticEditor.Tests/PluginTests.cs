using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;
using AbioticEditor.Plugins.Events;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers the host-side plugin machinery that does not require loading a third-party
/// assembly: manifest parsing/validation, discovery, save-kind detection, and the
/// save-operation runner (driven by an in-test operation). The full reflection/load path
/// is exercised end to end by the CLI against the shipped sample plugins.
/// </summary>
public sealed class PluginTests
{
    // ---------- manifest IO ----------

    [Fact]
    public void ManifestIo_RoundTrips_AndValidates()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "plugin.json");
        File.WriteAllText(path, """
            {
              "id": "com.test.sample",
              "name": "Sample",
              "version": "1.2.3",
              "entryAssembly": "Sample.dll",
              "capabilities": ["saveOperation"],
              "enabled": true
            }
            """);

        var manifest = PluginManifestIo.TryRead(path);
        Assert.NotNull(manifest);
        Assert.Equal("com.test.sample", manifest!.Id);
        Assert.Equal("Sample.dll", manifest.EntryAssembly);
        Assert.Contains(PluginCapabilities.SaveOperation, manifest.Capabilities);
        Assert.True(manifest.Enabled);
    }

    [Theory]
    [InlineData("{ \"name\": \"x\", \"entryAssembly\": \"a.dll\" }")]            // no id
    [InlineData("{ \"id\": \"x\", \"name\": \"x\" }")]                             // no entry assembly
    [InlineData("{ \"id\": \"x\", \"entryAssembly\": \"..\\\\evil.dll\" }")]      // path traversal
    [InlineData("{ \"id\": \"x\", \"entryAssembly\": \"a.txt\" }")]               // not a dll
    [InlineData("not json at all")]                                               // unparseable
    public void ManifestIo_RejectsBadManifests(string json)
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "plugin.json");
        File.WriteAllText(path, json);
        Assert.Null(PluginManifestIo.TryRead(path));
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1.2", "1.2")]
    [InlineData("v2.3.4", "2.3.4")]
    [InlineData("1.0.0-beta.2", "1.0.0")]
    public void NormalizeVersion_CoercesFriendlySpellings(string input, string expected)
        => Assert.Equal(expected, PluginManifestIo.NormalizeVersion(input));

    // ---------- discovery ----------

    [Fact]
    public void DiscoverManifests_FindsValid_SkipsDisabledAwareAndInvalid()
    {
        using var root = new TempDir();
        WritePlugin(root.Path, "good", "com.test.good", enabled: true);
        WritePlugin(root.Path, "off", "com.test.off", enabled: false);
        // An invalid manifest folder should simply be ignored, not throw.
        Directory.CreateDirectory(Path.Combine(root.Path, "broken"));
        File.WriteAllText(Path.Combine(root.Path, "broken", "plugin.json"), "{ not valid");

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manifests = PluginManager.DiscoverManifests();
            Assert.Contains(manifests, m => m.Id == "com.test.good");
            // Disabled plugins are still DISCOVERED (so they can be listed/re-enabled).
            Assert.Contains(manifests, m => m.Id == "com.test.off" && !m.Enabled);
            Assert.DoesNotContain(manifests, m => m.Id == "broken");
        }
    }

    [Fact]
    public void Discovery_DedupesById_UserRootWins()
    {
        using var userRoot = new TempDir();
        using var bundledRoot = new TempDir();
        WritePlugin(userRoot.Path, "a", "com.test.dup", enabled: true);
        WritePlugin(bundledRoot.Path, "b", "com.test.dup", enabled: false);

        // Roots are searched user-first; the first sighting of an id wins.
        var combined = userRoot.Path + Path.PathSeparator + bundledRoot.Path;
        using (new EnvScope("ABIOTIC_PLUGINS_DIR", combined))
        {
            var manifests = PluginManager.DiscoverManifests();
            var dup = Assert.Single(manifests, m => m.Id == "com.test.dup");
            Assert.True(dup.Enabled, "the user-root (enabled) copy should shadow the bundled one.");
        }
    }

    // ---------- save kind detection ----------

    [Fact]
    public void SaveKindDetector_ClassifiesByClassName()
    {
        Assert.Equal(SaveKind.Player, SaveKindDetector.FromSaveClass("/Game/...Abiotic_CharacterSave_C"));
        Assert.Equal(SaveKind.World, SaveKindDetector.FromSaveClass("/Game/...Abiotic_WorldSave_C"));
        Assert.Equal(SaveKind.Metadata, SaveKindDetector.FromSaveClass("/Game/...Abiotic_WorldMetadataSave_C"));
        Assert.Equal(SaveKind.Any, SaveKindDetector.FromSaveClass(null));
    }

    [Theory]
    [InlineData(SaveKind.Any, SaveKind.World, true)]
    [InlineData(SaveKind.Player, SaveKind.Player, true)]
    [InlineData(SaveKind.Player, SaveKind.World, false)]
    public void SaveKindDetector_Matches(SaveKind appliesTo, SaveKind actual, bool expected)
        => Assert.Equal(expected, SaveKindDetector.Matches(appliesTo, actual));

    // ---------- save operation runner ----------

    [Fact]
    public async Task Runner_WritesAndBacksUp_WhenOperationMarksChanged()
    {
        if (Fixtures.CascadeDir is null) return; // requires the save fixtures
        using var temp = new TempDir();
        var save = Path.Combine(temp.Path, "Player_test.sav");
        File.Copy(Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav"), save);

        var op = new TouchSkillOperation();
        var outcome = await SaveOperationRunner.RunAsync(op, save, null, NoopLog.Instance);

        Assert.True(outcome.Result.Success);
        Assert.True(outcome.Wrote);
        Assert.True(File.Exists(save + ".bak"), "a backup must be kept on write.");
        Assert.Equal(SaveKind.Player, outcome.Kind);
    }

    [Fact]
    public async Task Runner_DryRun_DoesNotWrite()
    {
        if (Fixtures.CascadeDir is null) return;
        using var temp = new TempDir();
        var save = Path.Combine(temp.Path, "Player_test.sav");
        var source = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        File.Copy(source, save);
        var before = File.GetLastWriteTimeUtc(save);

        var outcome = await SaveOperationRunner.RunAsync(new TouchSkillOperation(), save, null, NoopLog.Instance, dryRun: true);

        Assert.True(outcome.Result.Success);
        Assert.False(outcome.Wrote);
        Assert.False(File.Exists(save + ".bak"));
        Assert.Equal(before, File.GetLastWriteTimeUtc(save));
    }

    [Fact]
    public async Task Runner_RefusesWrongSaveKind()
    {
        if (Fixtures.CascadeDir is null) return;
        var world = Directory.EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav", SearchOption.AllDirectories).FirstOrDefault();
        if (world is null) return;

        var outcome = await SaveOperationRunner.RunAsync(new TouchSkillOperation(), world, null, NoopLog.Instance);

        Assert.False(outcome.Result.Success);
        Assert.False(outcome.Wrote);
        Assert.Contains("Player", outcome.Result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runner_NoChange_SkipsWrite()
    {
        if (Fixtures.CascadeDir is null) return;
        using var temp = new TempDir();
        var save = Path.Combine(temp.Path, "Player_test.sav");
        File.Copy(Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav"), save);

        // An operation that never marks a change must not produce a write or a backup.
        var outcome = await SaveOperationRunner.RunAsync(new NoOpOperation(), save, null, NoopLog.Instance);

        Assert.True(outcome.Result.Success);
        Assert.False(outcome.Wrote);
        Assert.False(File.Exists(save + ".bak"));
    }

    [Fact]
    public async Task Runner_EnforcesRequiredParameters()
    {
        if (Fixtures.CascadeDir is null) return;
        using var temp = new TempDir();
        var save = Path.Combine(temp.Path, "Player_test.sav");
        File.Copy(Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav"), save);

        var outcome = await SaveOperationRunner.RunAsync(new RequiresParamOperation(), save, null, NoopLog.Instance);

        Assert.False(outcome.Result.Success);
        Assert.Contains("required parameter", outcome.Result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- JavaScript runtime + events ----------

    private const string SampleJs = """
        abiotic.registerSaveOperation({
            id: "js-money",
            displayName: "JS Money",
            appliesTo: "Player",
            parameters: [ { name: "amount", default: "1234" } ],
            execute: function (ctx) {
                ctx.player.money = parseInt(ctx.getParameter("amount", "1234"), 10);
                return { message: "set money", changeCount: 1 };
            }
        });
        abiotic.registerCommand({
            name: "js-echo",
            description: "echo",
            invoke: function (ctx) { ctx.print("echo from js"); return 0; }
        });
        abiotic.registerMenuAction({ id: "js-act", title: "JS Action", invoke: function (ctx) { } });
        abiotic.on("save.written", function (event) {
            abiotic.log.info("JSEVENT handled " + event.name + " path=" + event.savePath);
        });
        """;

    private static void WriteJsPlugin(string root, string folder, string id, string script)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.js"), script);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "runtime": "javascript",
              "entryScript": "plugin.js",
              "capabilities": ["saveOperation", "consoleCommand", "menuAction", "eventHandler"],
              "enabled": true
            }
            """);
    }

    [Fact]
    public void JavaScriptPlugin_LoadsAndRegistersAllCapabilities()
    {
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "js", "com.test.js", SampleJs);

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");

            var descriptor = Assert.Single(manager.Descriptors);
            Assert.Equal(PluginLoadState.Loaded, descriptor.State);
            Assert.Single(descriptor.SaveOperations);
            Assert.Single(descriptor.ConsoleCommands);
            Assert.Single(descriptor.MenuActions);
            Assert.Single(descriptor.EventHandlers);
            Assert.Equal("js-money", descriptor.SaveOperations[0].Id);
            Assert.Equal(SaveKind.Player, descriptor.SaveOperations[0].AppliesTo);
        }
    }

    [Fact]
    public async Task JavaScriptSaveOperation_EditsAndPersists()
    {
        if (Fixtures.CascadeDir is null) return;
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "js", "com.test.js", SampleJs);

        using var temp = new TempDir();
        var save = Path.Combine(temp.Path, "Player_test.sav");
        File.Copy(Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav"), save);

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");
            var op = manager.SaveOperations.Single(c => c.Value.Id == "js-money");

            var outcome = await SaveOperationRunner.RunAsync(
                op.Value, save, new Dictionary<string, string> { ["amount"] = "7777" }, op.Plugin.Host!.Log);

            Assert.True(outcome.Result.Success);
            Assert.True(outcome.Wrote);
            // The JS edit really landed: re-read the money field from disk.
            Assert.Equal(7777, PlayerSaveReader.ReadFromFile(save).Stats.Money);
        }
    }

    [Fact]
    public void RaiseEvent_DispatchesToJavaScriptHandler()
    {
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "js", "com.test.js", SampleJs);
        using var logDir = new TempDir();

        var wasEnabled = EditorLog.Enabled;
        var previousDir = EditorLog.LogDirectory;
        try
        {
            EditorLog.LogDirectory = logDir.Path;
            EditorLog.Enabled = true; // so the JS handler's log line lands on disk

            using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
            {
                var manager = new PluginManager();
                manager.EnsureLoaded("test");
                manager.RaiseEvent(PluginEvents.SaveWritten, new Dictionary<string, object?>
                {
                    ["savePath"] = "C:/x/Player.sav",
                    ["saveKind"] = SaveKind.Player,
                });
            }

            var logText = string.Concat(Directory.EnumerateFiles(logDir.Path, "editor-*.log").Select(File.ReadAllText));
            Assert.Contains("JSEVENT handled save.written", logText);
            Assert.Contains("Player.sav", logText);
        }
        finally
        {
            EditorLog.Enabled = wasEnabled;
            EditorLog.LogDirectory = previousDir;
        }
    }

    private const string WebToolJs = """
        abiotic.registerWebTool({
            id: "wt",
            title: "WT",
            glyph: "🌐",
            html: "<h1>hello web</h1>",
            handleMessage: function (message, ctx) {
                var r;
                try { r = JSON.parse(message); } catch (e) { r = {}; }
                if (r.type === "ping") return JSON.stringify({ pong: true, kind: ctx.activeSaveKind });
                if (r.type === "summary") return ctx.playerSummaryJson();
                return "{}";
            }
        });
        """;

    [Fact]
    public async Task JavaScriptWebTool_RegistersContentAndBridge()
    {
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "wt", "com.test.wt", WebToolJs);

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");
            var tool = Assert.Single(manager.WebTools).Value;
            Assert.Equal("wt", tool.Id);

            var ctx = new FakeWebContext(manager.Descriptors[0].Host!, null, null, null);

            // Inline HTML content is delivered as-is.
            var content = tool.CreateContent(ctx);
            Assert.Contains("hello web", content.Html);

            // The bridge round-trips: a request payload reaches handleMessage and the reply returns.
            var reply = await tool.HandleMessageAsync("{\"type\":\"ping\"}", ctx);
            Assert.Contains("\"pong\":true", reply);
        }
    }

    [Fact]
    public async Task JavaScriptWebTool_ReadsPlayerSummaryThroughBridge()
    {
        if (Fixtures.CascadeDir is null) return;
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "wt", "com.test.wt", WebToolJs);

        var savePath = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        using var fs = File.OpenRead(savePath);
        Core.SaveClasses.AbioticSaveClasses.EnsureLoaded();
        var save = SaveGame.LoadFrom(fs);

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");
            var tool = manager.WebTools.Single().Value;
            var ctx = new FakeWebContext(manager.Descriptors[0].Host!, savePath, SaveKind.Player, save);

            var reply = await tool.HandleMessageAsync("{\"type\":\"summary\"}", ctx);
            Assert.Contains("\"money\":", reply);
            Assert.Contains("\"skills\":", reply);
        }
    }

    [Fact]
    public async Task JavaScript_CanDriveAppUiThroughHostBridge()
    {
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "ui", "com.test.ui", """
            abiotic.registerCommand({
                name: "ui-test",
                invoke: function (ctx) {
                    abiotic.ui.showAlert("T", "M");
                    abiotic.ui.toast("hi");
                    abiotic.ui.runSaveOperation("op-x");
                    return 0;
                }
            });
            """);

        var fakeUi = new RecordingHostUi();
        var previous = PluginHostEnvironment.HostUi;
        try
        {
            PluginHostEnvironment.HostUi = fakeUi;
            using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
            {
                var manager = new PluginManager();
                manager.EnsureLoaded("test");
                var command = manager.ConsoleCommands.Single(c => c.Value.Name == "ui-test").Value;
                await command.InvokeAsync(new FakeConsoleContext(manager.Descriptors[0].Host!));
            }

            Assert.Contains(("T", "M"), fakeUi.Alerts);
            Assert.Contains("hi", fakeUi.Toasts);
            Assert.Contains("op-x", fakeUi.RanOperations);
        }
        finally
        {
            PluginHostEnvironment.HostUi = previous;
        }
    }

    [Fact]
    public void RaiseEvent_IsolatesThrowingHandlers()
    {
        // A handler that throws must not stop the event from being raised or crash the host.
        using var root = new TempDir();
        WriteJsPlugin(root.Path, "js", "com.test.js",
            "abiotic.on('app.started', function (e) { throw new Error('boom'); });");

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");
            // Should not throw despite the handler throwing.
            manager.RaiseEvent(PluginEvents.AppStarted);
        }
    }

    // ---------- helpers ----------

    private static void WritePlugin(string root, string folder, string id, bool enabled)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            {
              "id": "{{id}}",
              "name": "{{id}}",
              "version": "1.0.0",
              "entryAssembly": "{{folder}}.dll",
              "enabled": {{(enabled ? "true" : "false")}}
            }
            """);
    }

    /// <summary>A minimal operation that marks one change (mutates a property in place).</summary>
    private sealed class TouchSkillOperation : ISaveOperation
    {
        public string Id => "touch";
        public string DisplayName => "Touch";
        public string Description => "test operation";
        public SaveKind AppliesTo => SaveKind.Player;

        public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
        {
            // Any in-place edit; the point is to exercise MarkChanged -> write.
            _ = context.Save.Properties; // touch the model
            context.MarkChanged();
            return Task.FromResult(SaveOperationResult.Ok("touched", 1));
        }
    }

    private sealed class NoOpOperation : ISaveOperation
    {
        public string Id => "noop";
        public string DisplayName => "No-op";
        public string Description => "changes nothing";
        public SaveKind AppliesTo => SaveKind.Player;

        public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(SaveOperationResult.NoChange("nothing to do"));
    }

    private sealed class RequiresParamOperation : ISaveOperation
    {
        public string Id => "needs-param";
        public string DisplayName => "Needs param";
        public string Description => "requires a parameter";
        public SaveKind AppliesTo => SaveKind.Player;

        public IReadOnlyList<SaveOperationParameter> Parameters { get; } = new[]
        {
            new SaveOperationParameter("count", "required count", Required: true),
        };

        public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(SaveOperationResult.Ok("should not reach here"));
    }

    private sealed class NoopLog : IPluginLog
    {
        public static readonly NoopLog Instance = new();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    /// <summary>Records app-UI bridge calls so tests can assert a plugin drove the app.</summary>
    private sealed class RecordingHostUi : IHostUi
    {
        public List<(string Title, string Message)> Alerts { get; } = new();
        public List<string> Toasts { get; } = new();
        public List<string> RanOperations { get; } = new();

        public string? OpenSavePath => null;
        public Task ShowAlertAsync(string title, string message) { Alerts.Add((title, message)); return Task.CompletedTask; }
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ToastAsync(string message) { Toasts.Add(message); return Task.CompletedTask; }
        public Task<bool> RunSaveOperationAsync(string operationId, IReadOnlyDictionary<string, string>? parameters = null)
        { RanOperations.Add(operationId); return Task.FromResult(true); }
        public Task ReloadOpenSaveAsync() => Task.CompletedTask;
        public Task OpenSettingsAsync() => Task.CompletedTask;
        public Task OpenPluginsPanelAsync() => Task.CompletedTask;
    }

    private sealed class FakeConsoleContext : IConsoleCommandContext
    {
        public FakeConsoleContext(IPluginHost host) => Host = host;
        public IReadOnlyDictionary<string, string> Arguments { get; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string?> Options { get; } = new Dictionary<string, string?>();
        public TextWriter Out => TextWriter.Null;
        public TextWriter Error => TextWriter.Null;
        public IPluginHost Host { get; }
    }

    /// <summary>Minimal <see cref="IWebToolContext"/> for driving a web tool's bridge in tests.</summary>
    private sealed class FakeWebContext : IWebToolContext
    {
        public FakeWebContext(IPluginHost host, string? path, SaveKind? kind, SaveGame? save)
        {
            Host = host;
            ActiveSavePath = path;
            ActiveSaveKind = kind;
            ActiveSave = save;
        }

        public IPluginHost Host { get; }
        public string? ActiveSavePath { get; }
        public SaveKind? ActiveSaveKind { get; }
        public SaveGame? ActiveSave { get; }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Directory.CreateDirectory(Path);

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-plugin-test-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
