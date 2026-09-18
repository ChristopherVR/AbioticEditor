using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// General research dump for grounding a new live-editing area: for every package whose path
/// contains one of the <c>LIVE_GAP_PROBE_FRAGMENTS</c> (comma-separated) it writes the class
/// layout (properties + function names, with each function's parameters) to
/// <c>layouts.txt</c>, and for every package whose file stem is listed in
/// <c>LIVE_GAP_PROBE_JSON</c> (comma-separated) it also writes the full exports with blueprint
/// bytecode as <c>&lt;stem&gt;.json</c>. Set <c>LIVE_GAP_PROBE_OUT</c> to a directory to run it.
/// Never guess a live property name from a save leaf name - read it from this dump instead.
/// </summary>
public sealed class LiveGapProbe
{
    [Fact]
    public void Dump_requested_class_layouts()
    {
        var output = Environment.GetEnvironmentVariable("LIVE_GAP_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var fragments = Split(Environment.GetEnvironmentVariable("LIVE_GAP_PROBE_FRAGMENTS"));
        var jsonStems = Split(Environment.GetEnvironmentVariable("LIVE_GAP_PROBE_JSON"));
        if (fragments.Length == 0 && jsonStems.Length == 0) return;

        var paks = AfInstallLocator.FindPaksDirectory();
        Assert.NotNull(paks);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(GameAssetProvider.FindConventionalMappings()!);
        provider.ReadScriptData = true;
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));
        Directory.CreateDirectory(output);

        var keys = provider.Files.Keys
            .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(k => fragments.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase))
                        || jsonStems.Contains(Path.GetFileNameWithoutExtension(k), StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var layouts = new StreamWriter(Path.Combine(output, "layouts.txt"), false);
        layouts.WriteLine($"matching packages: {keys.Count}");
        foreach (var key in keys)
        {
            UObject[] exports;
            try { exports = provider.LoadPackage(key).GetExports().ToArray(); }
            catch (Exception ex) { layouts.WriteLine($"--- {key}: load failed ({ex.GetType().Name}: {ex.Message})"); continue; }

            layouts.WriteLine($"--- {key}");
            foreach (var export in exports)
            {
                if (export is UStruct st)
                {
                    layouts.WriteLine($"  STRUCT {export.Name} ({export.ExportType}) super={st.SuperStruct?.Name}");
                    foreach (var c in st.ChildProperties ?? [])
                        layouts.WriteLine($"    prop {c.Name.Text} : {c.GetType().Name}");
                    foreach (var c in st.Children ?? [])
                        layouts.WriteLine($"    func {c.Name}");
                }
                else if (export is UEnum en)
                {
                    layouts.WriteLine($"  ENUM {en.Name} ({en.Names.Length} values):");
                    foreach (var (n, v) in en.Names)
                        layouts.WriteLine($"    [{v}] {n.Text}");
                }
            }

            var stem = Path.GetFileNameWithoutExtension(key);
            if (jsonStems.Contains(stem, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    File.WriteAllText(Path.Combine(output, stem + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(output, stem + ".error.txt"), ex.ToString());
                }
            }
        }
    }

    private static string[] Split(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
