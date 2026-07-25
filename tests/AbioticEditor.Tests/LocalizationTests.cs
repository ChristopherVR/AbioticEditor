using System.IO;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;

namespace AbioticEditor.Tests;

/// <summary>
/// Localization coverage: the plugin contribution layer (resx/json packs, AddLocalization, the
/// JavaScript bridge) and host-neutral plugin localization behavior.
///
/// PluginLocalizations is process-global, so each test Clears it first to isolate.
/// </summary>
public sealed class LocalizationTests
{
    // ---------- PluginLocalizations registry ----------

    [Fact]
    public void Add_Then_Lookup_ReturnsContributedValue()
    {
        PluginLocalizations.Clear();
        PluginLocalizations.Add("de", new Dictionary<string, string> { ["Common_Save"] = "Speichern" });

        Assert.Equal("Speichern", PluginLocalizations.Lookup("de", "Common_Save"));
        Assert.Null(PluginLocalizations.Lookup("de", "Missing_Key"));
        Assert.Null(PluginLocalizations.Lookup("fr", "Common_Save"));
        PluginLocalizations.Clear();
    }

    [Fact]
    public void Lookup_FallsBackFromRegionToNeutralCulture()
    {
        PluginLocalizations.Clear();
        PluginLocalizations.Add("de", new Dictionary<string, string> { ["Common_Save"] = "Speichern" });

        // A pack shipped for "de" should answer a "de-DE" / "de-AT" UI culture.
        Assert.Equal("Speichern", PluginLocalizations.Lookup("de-DE", "Common_Save"));
        Assert.Equal("Speichern", PluginLocalizations.Lookup("de-AT", "Common_Save"));
        PluginLocalizations.Clear();
    }

    [Fact]
    public void Add_IsCaseInsensitiveOnCulture_AndLastWriteWins()
    {
        PluginLocalizations.Clear();
        PluginLocalizations.Add("DE", new Dictionary<string, string> { ["Common_Save"] = "Erste" });
        PluginLocalizations.Add("de", new Dictionary<string, string> { ["Common_Save"] = "Zweite" });

        // Same culture (case-insensitive) - the later contribution overrides the earlier.
        Assert.Equal("Zweite", PluginLocalizations.Lookup("de", "Common_Save"));
        PluginLocalizations.Clear();
    }

    [Fact]
    public void EmptyTable_LookupReturnsNull_AndClearRaisesNoFalsePositive()
    {
        PluginLocalizations.Clear();
        Assert.Null(PluginLocalizations.Lookup("de", "Common_Save"));
        Assert.Empty(PluginLocalizations.Cultures);
    }

    [Fact]
    public void Changed_FiresOnAddAndClear()
    {
        PluginLocalizations.Clear();
        var fired = 0;
        void Handler() => fired++;
        PluginLocalizations.Changed += Handler;
        try
        {
            PluginLocalizations.Add("de", new Dictionary<string, string> { ["K"] = "V" });
            PluginLocalizations.Clear();
            Assert.Equal(2, fired); // one Add + one Clear (Clear only fires when it had content)
        }
        finally
        {
            PluginLocalizations.Changed -= Handler;
            PluginLocalizations.Clear();
        }
    }

    // ---------- resource-only "localization" runtime plugin ----------

    [Fact]
    public void LocalizationPlugin_Json_LoadsAndMergesStrings()
    {
        PluginLocalizations.Clear();
        using var root = new TempDir();
        var dir = Path.Combine(root.Path, "it-pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """
            {
              "id": "com.test.it",
              "name": "Italian",
              "version": "1.0.0",
              "runtime": "localization",
              "localizations": { "it": "strings.it.json" }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "strings.it.json"),
            """{ "Common_Save": "SALVA", "Common_Close": "CHIUDI", "_note": 123 }""");

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");

            var descriptor = Assert.Single(manager.Descriptors);
            Assert.Equal(PluginLoadState.Loaded, descriptor.State);
            Assert.Single(descriptor.Localizations);
            Assert.Equal("it", descriptor.Localizations[0].Culture);

            Assert.Equal("SALVA", PluginLocalizations.Lookup("it", "Common_Save"));
            Assert.Equal("CHIUDI", PluginLocalizations.Lookup("it", "Common_Close"));
            // The non-string "_note" property is ignored, not loaded as a string.
            Assert.Null(PluginLocalizations.Lookup("it", "_note"));
        }
        PluginLocalizations.Clear();
    }

    [Fact]
    public void LocalizationPlugin_Resx_LoadsAndMergesStrings()
    {
        PluginLocalizations.Clear();
        using var root = new TempDir();
        var dir = Path.Combine(root.Path, "fr-pack");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """
            {
              "id": "com.test.fr",
              "name": "French",
              "version": "1.0.0",
              "runtime": "localization",
              "localizations": { "fr": "strings.fr.resx" }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "strings.fr.resx"), """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Common_Save" xml:space="preserve"><value>ENREGISTRER</value></data>
            </root>
            """);

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");

            Assert.Equal(PluginLoadState.Loaded, Assert.Single(manager.Descriptors).State);
            Assert.Equal("ENREGISTRER", PluginLocalizations.Lookup("fr", "Common_Save"));
        }
        PluginLocalizations.Clear();
    }

    [Fact]
    public void JavaScriptPlugin_AddLocalization_ContributesStrings()
    {
        PluginLocalizations.Clear();
        using var root = new TempDir();
        var dir = Path.Combine(root.Path, "js-loc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.js"),
            "abiotic.addLocalization('es', { Common_Save: 'GUARDAR', Common_Cancel: 'Cancelar' });");
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """
            {
              "id": "com.test.jsloc",
              "name": "JS loc",
              "version": "1.0.0",
              "runtime": "javascript",
              "entryScript": "plugin.js"
            }
            """);

        using (new EnvScope("ABIOTIC_PLUGINS_DIR", root.Path))
        {
            var manager = new PluginManager();
            manager.EnsureLoaded("test");

            var descriptor = Assert.Single(manager.Descriptors);
            Assert.Equal(PluginLoadState.Loaded, descriptor.State);
            Assert.Equal("GUARDAR", PluginLocalizations.Lookup("es", "Common_Save"));
            Assert.Equal("Cancelar", PluginLocalizations.Lookup("es", "Common_Cancel"));
        }
        PluginLocalizations.Clear();
    }

    // ---------- manifest validation for the localization runtime ----------

    [Fact]
    public void Validate_LocalizationManifest_Passes()
    {
        var manifest = new PluginManifest
        {
            Id = "com.test.loc",
            Runtime = PluginRuntimes.Localization,
            Localizations = new Dictionary<string, string> { ["de"] = "de.json" },
        };
        Assert.Null(PluginManifestIo.Validate(manifest));
    }

    [Fact]
    public void Validate_LocalizationManifest_RequiresAtLeastOneFile()
    {
        var manifest = new PluginManifest { Id = "com.test.loc", Runtime = PluginRuntimes.Localization };
        Assert.Contains("localizations", PluginManifestIo.Validate(manifest));
    }

    [Theory]
    [InlineData("../escape.json", "not a path")]
    [InlineData("sub/dir.json", "not a path")]
    [InlineData("strings.txt", ".json or .resx")]
    public void Validate_RejectsBadLocalizationFile(string file, string expectedFragment)
    {
        var manifest = new PluginManifest
        {
            Id = "com.test.loc",
            Runtime = PluginRuntimes.Localization,
            Localizations = new Dictionary<string, string> { ["de"] = file },
        };
        var error = PluginManifestIo.Validate(manifest);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }

    // ---------- helpers ----------

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Directory.CreateDirectory(Path);

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-loc-test-" + Guid.NewGuid().ToString("N"));

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
