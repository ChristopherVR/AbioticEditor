using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;

namespace AbioticEditor.Tests;

/// <summary>
/// Localization coverage: the plugin contribution layer (resx/json packs, AddLocalization, the
/// JavaScript bridge) plus "renders as expected" guards over the resources themselves - every
/// {loc:Localize Key} the XAML references resolves to a real string, and the shipped locales
/// cover the full key set, so the UI never shows a raw key or an unexpected gap.
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

    // ---------- "renders as expected" guards over the shipped resources ----------

    [Fact]
    public void EveryLocalizeKeyInXaml_HasANeutralResourceEntry()
    {
        var app = AppDir();
        Assert.NotNull(app);

        var neutral = ReadResx(Path.Combine(app!, "Localization", "AppResources.resx"));
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var xaml in EnumerateXaml(app))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(xaml), @"\{loc:Localize (\w+)\}"))
            {
                var key = m.Groups[1].Value;
                if (!neutral.ContainsKey(key))
                {
                    missing.Add(key);
                }
            }
        }

        // A missing key makes the UI render the raw key text (e.g. "PlayerGeneral_General"),
        // so this must stay empty.
        Assert.True(missing.Count == 0, $"{missing.Count} {{loc:Localize}} key(s) absent from AppResources.resx: {string.Join(", ", missing)}");
    }

    [Fact]
    public void NeutralResx_HasNoDuplicateKeys()
    {
        var app = AppDir();
        Assert.NotNull(app);
        var path = Path.Combine(app!, "Localization", "AppResources.resx");

        var names = XDocument.Load(path).Root!.Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .Where(n => n is not null)
            .ToList();
        var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, $"duplicate resx keys: {string.Join(", ", dupes)}");
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    public void ShippedLocale_IsWellFormed_CoversNeutralKeys_AndHasNoOrphans(string culture)
    {
        var app = AppDir();
        Assert.NotNull(app);

        var neutral = ReadResx(Path.Combine(app!, "Localization", "AppResources.resx"));
        var localePath = Path.Combine(app!, "Localization", $"AppResources.{culture}.resx");
        Assert.True(File.Exists(localePath), $"missing locale file: {localePath}");

        var locale = ReadResx(localePath); // throws if malformed XML

        // No orphan keys (a locale key that no longer exists in the neutral file).
        var orphans = locale.Keys.Where(k => !neutral.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(orphans.Count == 0, $"{culture}: orphan keys not in neutral: {string.Join(", ", orphans)}");

        // Full coverage: every neutral key is translated (present), so the UI never falls back
        // to English for a shipped language.
        var untranslated = neutral.Keys.Where(k => !locale.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(untranslated.Count == 0,
            $"{culture}: {untranslated.Count} neutral key(s) missing a translation: {string.Join(", ", untranslated.Take(20))}");
    }

    // ---------- helpers ----------

    private static string? AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AbioticEditor.App");
            if (File.Exists(Path.Combine(candidate, "Localization", "AppResources.resx")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateXaml(string appDir)
    {
        foreach (var f in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            var norm = f.Replace('\\', '/');
            if (!norm.Contains("/bin/") && !norm.Contains("/obj/"))
            {
                yield return f;
            }
        }
    }

    private static Dictionary<string, string> ReadResx(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in XDocument.Load(path).Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (name is not null)
            {
                dict[name] = data.Element("value")?.Value ?? string.Empty;
            }
        }
        return dict;
    }

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
