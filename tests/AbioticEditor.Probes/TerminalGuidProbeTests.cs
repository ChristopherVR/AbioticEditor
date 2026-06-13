using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe: can the player save's TerminalRespawnID GUIDs (NameProperty, 32 hex chars)
/// be resolved to static respawn-terminal actors baked into cooked level packages?
/// Fixture GUIDs (Cascade): 95CAED254C17360B69B3738E468CD49C (3 players), 35DCF84F4AC366B8DCBB61A93D9C83C0.
/// </summary>
public class TerminalGuidProbeTests
{
    private readonly ITestOutputHelper _output;

    public TerminalGuidProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string[] TargetGuids =
    [
        "95CAED254C17360B69B3738E468CD49C",
        "35DCF84F4AC366B8DCBB61A93D9C83C0",
    ];

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return null;

#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    /// <summary>FGuid digits string -> serialized byte pattern (4 x uint32, little-endian each).</summary>
    private static byte[] GuidBytes(string digits)
    {
        var bytes = new byte[16];
        for (var part = 0; part < 4; part++)
        {
            var v = uint.Parse(digits.Substring(part * 8, 8), System.Globalization.NumberStyles.HexNumber);
            bytes[part * 4 + 0] = (byte)(v & 0xFF);
            bytes[part * 4 + 1] = (byte)((v >> 8) & 0xFF);
            bytes[part * 4 + 2] = (byte)((v >> 16) & 0xFF);
            bytes[part * 4 + 3] = (byte)((v >> 24) & 0xFF);
        }
        return bytes;
    }

    /// <summary>
    /// All byte patterns a target GUID could appear as: serialized FGuid (4 x uint32 LE),
    /// big-endian per part, raw .NET Guid layout, ASCII and UTF-16 of the digits string.
    /// </summary>
    private static List<(string Label, byte[] Bytes)> AllPatterns()
    {
        var patterns = new List<(string, byte[])>();
        foreach (var g in TargetGuids)
        {
            patterns.Add(($"{g}/fguid-le", GuidBytes(g)));
            patterns.Add(($"{g}/be", Convert.FromHexString(g)));
            patterns.Add(($"{g}/ascii", System.Text.Encoding.ASCII.GetBytes(g)));
            patterns.Add(($"{g}/ascii-lower", System.Text.Encoding.ASCII.GetBytes(g.ToLowerInvariant())));
        }
        return patterns;
    }

    [Fact]
    public void Probe1_ListRespawnAssets_AndMaps()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var afFiles = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _output.WriteLine("=== uasset paths containing respawn/spawnpoint/terminal ===");
        foreach (var k in afFiles.Where(p =>
                     p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                     (p.Contains("respawn", StringComparison.OrdinalIgnoreCase) ||
                      p.Contains("spawnpoint", StringComparison.OrdinalIgnoreCase) ||
                      p.Contains("terminal", StringComparison.OrdinalIgnoreCase))))
        {
            _output.WriteLine($"  {k}");
        }

        _output.WriteLine("");
        _output.WriteLine("=== .umap packages (with sizes) ===");
        foreach (var k in afFiles.Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var size = provider.Files[k].Size;
            var uexpKey = k[..^5] + ".uexp";
            var uexpSize = provider.Files.TryGetValue(uexpKey, out var uexp) ? uexp.Size : 0;
            _output.WriteLine($"  {k}  umap={size / 1024}KB uexp={uexpSize / 1024 / 1024}MB");
        }
    }

    /// <summary>
    /// Raw byte scan of every cooked map package (.umap + .uexp) for the serialized FGuid
    /// byte patterns of the fixture TerminalRespawnIDs. Cheap and exhaustive - a hit tells us
    /// the GUID is baked into level data and which level package to inspect.
    /// </summary>
    [Fact]
    public void Probe2_ByteScanMaps_ForTargetGuids()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var patterns = AllPatterns();

        var mapKeys = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase))
            .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hits = 0;
        foreach (var umapKey in mapKeys)
        {
            foreach (var key in new[] { umapKey, umapKey[..^5] + ".uexp" })
            {
                if (!provider.Files.TryGetValue(key, out var file)) continue;
                byte[] data;
                try { data = file.Read(); }
                catch (Exception ex) { _output.WriteLine($"READ FAIL {key}: {ex.Message}"); continue; }

                foreach (var (label, pat) in patterns)
                {
                    var idx = data.AsSpan().IndexOf(pat);
                    while (idx >= 0)
                    {
                        _output.WriteLine($"HIT {label} in {key} @ 0x{idx:X}");
                        hits++;
                        var next = data.AsSpan(idx + 1).IndexOf(pat);
                        idx = next >= 0 ? idx + 1 + next : -1;
                    }
                }
            }
        }
        _output.WriteLine($"Scanned {mapKeys.Count} map packages, total hits: {hits}");
    }

    /// <summary>
    /// Fallback breadth scan: byte-scan all non-map .uexp files under Blueprints/ and
    /// DataTables-ish paths in case the GUIDs live in a data asset rather than a level.
    /// </summary>
    [Fact]
    public void Probe3_ByteScanBlueprints_ForTargetGuids()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var patterns = AllPatterns();

        var keys = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/Content/Blueprints/", StringComparison.OrdinalIgnoreCase))
            .Where(p => p.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hits = 0;
        long bytes = 0;
        foreach (var key in keys)
        {
            byte[] data;
            try { data = provider.Files[key].Read(); }
            catch { continue; }
            bytes += data.Length;

            foreach (var (label, pat) in patterns)
            {
                if (data.AsSpan().IndexOf(pat) is var idx && idx >= 0)
                {
                    _output.WriteLine($"HIT {label} in {key} @ 0x{idx:X}");
                    hits++;
                }
            }
        }
        _output.WriteLine($"Scanned {keys.Count} blueprint uexp files ({bytes / 1024 / 1024}MB), hits: {hits}");
    }

    /// <summary>
    /// Byte-scan every fixture .sav (world, player, metadata) for the target GUIDs in every
    /// representation. Earlier research only string-scanned world saves; a respawn-terminal
    /// registration stored as a serialized FGuid (16 raw bytes) would have been missed.
    /// </summary>
    [Fact]
    public void Probe5_ByteScanFixtureSaves_AllRepresentations()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var patterns = AllPatterns();

        foreach (var sav in Directory.EnumerateFiles(Fixtures.CascadeDir!, "*.sav", SearchOption.AllDirectories))
        {
            var data = File.ReadAllBytes(sav);
            foreach (var (label, pat) in patterns)
            {
                var idx = data.AsSpan().IndexOf(pat);
                while (idx >= 0)
                {
                    _output.WriteLine($"HIT {label} in {Path.GetRelativePath(Fixtures.CascadeDir!, sav)} @ 0x{idx:X}");
                    var next = data.AsSpan(idx + 1).IndexOf(pat);
                    idx = next >= 0 ? idx + 1 + next : -1;
                }
            }
        }
        _output.WriteLine("scan complete");
    }

    /// <summary>
    /// For map packages that byte-scan positive (fill in MatchedMaps after Probe2), load the
    /// package and identify the export that carries the GUID: actor name, class, properties,
    /// and the root component's RelativeLocation.
    /// </summary>
    [Fact]
    public void Probe4_IdentifyActors_InMatchedMaps()
    {
        // Populated from Probe2 output.
        string[] matchedMaps =
        [
            "AbioticFactor/Content/Maps/Facility.umap", // ASCII FName hits for both GUIDs (Probe2)
        ];

        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        foreach (var mapKey in matchedMaps)
        {
            _output.WriteLine($"##### {mapKey} #####");
            IPackage pkg;
            try { pkg = provider.LoadPackage(mapKey); }
            catch (Exception ex) { _output.WriteLine($"  LOAD FAIL: {ex.Message}"); continue; }

            // Load exports one by one; tolerate per-export failures.
            var exports = new List<UObject>();
            foreach (var lazy in pkg.ExportsLazy)
            {
                try { if (lazy.Value is { } obj) exports.Add(obj); }
                catch { /* ignore */ }
            }
            _output.WriteLine($"  exports loaded: {exports.Count}/{pkg.ExportsLazy.Length}");

            var matched = new List<UObject>();
            foreach (var obj in exports)
            {
                // Name-based match (export name embedding the GUID).
                if (TargetGuids.Any(g => obj.Name.Contains(g, StringComparison.OrdinalIgnoreCase)))
                {
                    _output.WriteLine($"  NAME MATCH: {obj.Name} ({obj.Class?.Name})");
                    matched.Add(obj);
                    continue;
                }

                foreach (var tag in obj.Properties)
                {
                    var found = FindGuid(tag.Tag?.GenericValue);
                    if (found is null) continue;
                    _output.WriteLine($"  GUID MATCH {found}: export '{obj.Name}' class={obj.Class?.Name} prop={tag.Name.Text}");
                    matched.Add(obj);
                }
            }

            // Dump matched actors fully, plus any component whose Outer is the actor (for location).
            foreach (var actor in matched.Distinct())
            {
                _output.WriteLine($"  === {actor.Name} ({actor.Class?.Name}) ===");
                foreach (var tag in actor.Properties)
                {
                    var s = tag.Tag?.GenericValue?.ToString() ?? "(null)";
                    if (s.Length > 200) s = s[..200];
                    _output.WriteLine($"    {tag.Name.Text} = {s}");
                }
                foreach (var comp in exports.Where(e => e.Outer?.Name.Text == actor.Name))
                {
                    var loc = comp.Properties.FirstOrDefault(p => p.Name.Text == "RelativeLocation");
                    if (loc is not null)
                    {
                        _output.WriteLine($"    component {comp.Name}: RelativeLocation = {loc.Tag?.GenericValue}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Build the full GUID -> location table: find every map containing a
    /// Deployed_PunchCardTerminal_C instance (cheap ASCII scan of the name table),
    /// then load those maps and dump SpawnedAssetID / LocationName / world position.
    /// </summary>
    [Fact]
    public void Probe6_EnumerateAllPunchCardTerminals()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var classNamePattern = System.Text.Encoding.ASCII.GetBytes("Deployed_PunchCardTerminal");

        var candidateMaps = new List<string>();
        foreach (var key in provider.Files.Keys
                     .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase))
                     .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] data;
            try { data = provider.Files[key].Read(); }
            catch { continue; }
            if (data.AsSpan().IndexOf(classNamePattern) >= 0) candidateMaps.Add(key);
        }
        _output.WriteLine($"maps referencing Deployed_PunchCardTerminal: {string.Join(", ", candidateMaps)}");

        foreach (var mapKey in candidateMaps)
        {
            IPackage pkg;
            try { pkg = provider.LoadPackage(mapKey); }
            catch (Exception ex) { _output.WriteLine($"{mapKey}: LOAD FAIL {ex.Message}"); continue; }

            var exports = new List<UObject>();
            foreach (var lazy in pkg.ExportsLazy)
            {
                try { if (lazy.Value is { } obj) exports.Add(obj); }
                catch { /* ignore */ }
            }

            foreach (var actor in exports.Where(e =>
                         e.Class?.Name.Text.StartsWith("Deployed_PunchCardTerminal", StringComparison.OrdinalIgnoreCase) == true))
            {
                var id = actor.Properties.FirstOrDefault(p => p.Name.Text == "SpawnedAssetID")?.Tag?.GenericValue?.ToString();
                var loc = actor.Properties.FirstOrDefault(p => p.Name.Text == "LocationName")?.Tag?.GenericValue?.ToString();
                var root = exports.FirstOrDefault(e =>
                    e.Outer?.Name.Text == actor.Name &&
                    e.Properties.Any(p => p.Name.Text == "RelativeLocation"));
                var pos = root?.Properties.FirstOrDefault(p => p.Name.Text == "RelativeLocation")?.Tag?.GenericValue?.ToString();
                _output.WriteLine($"{Path.GetFileNameWithoutExtension(mapKey)} | {actor.Name} | SpawnedAssetID={id} | LocationName={loc} | pos={pos}");
            }
        }
    }

    /// <summary>Recursively search a CUE4Parse property value for one of the target FGuids.</summary>
    private static string? FindGuid(object? value)
    {
        switch (value)
        {
            case FGuid guid:
            {
                var s = guid.ToString(EGuidFormats.Digits);
                return TargetGuids.FirstOrDefault(g => string.Equals(g, s, StringComparison.OrdinalIgnoreCase));
            }
            case FScriptStruct ss:
                return FindGuid(ss.StructType);
            case FStructFallback sf:
                foreach (var p in sf.Properties)
                {
                    if (FindGuid(p.Tag?.GenericValue) is { } hit) return hit;
                }
                return null;
            case UScriptArray arr:
                foreach (var el in arr.Properties)
                {
                    if (FindGuid(el.GenericValue) is { } hit) return hit;
                }
                return null;
            case UScriptMap map:
                foreach (var kv in map.Properties)
                {
                    if (FindGuid(kv.Key.GenericValue) is { } k) return k;
                    if (FindGuid(kv.Value?.GenericValue) is { } v) return v;
                }
                return null;
            case CUE4Parse.UE4.Objects.UObject.FName name:
                return TargetGuids.FirstOrDefault(g => name.Text.Contains(g, StringComparison.OrdinalIgnoreCase));
            case string str:
                return TargetGuids.FirstOrDefault(g => str.Contains(g, StringComparison.OrdinalIgnoreCase));
            default:
                return null;
        }
    }
}
