using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Probe: find door blueprint assets + the E_DoorStates enum so we can build a catalog.
/// Output-only - these aren't pass/fail, just discovery.
/// </summary>
public class DoorProbeTests
{
    private readonly ITestOutputHelper _output;

    public DoorProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_FindDoorBlueprintsAndEnum()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null)
        {
            _output.WriteLine("No paks available, skipping");
            return;
        }

        var mappings = GameAssetProvider.FindConventionalMappings();

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        if (mappings is not null)
        {
            provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        }
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        _output.WriteLine($"Mappings: {(mappings is null ? "MISSING" : mappings)}");

        // Look for E_DoorStates
        _output.WriteLine("");
        _output.WriteLine("=== E_DoorStates candidates ===");
        var enumCandidates = provider.Files.Keys
            .Where(p => p.Contains("E_DoorStates", StringComparison.OrdinalIgnoreCase))
            .Take(20).ToList();
        foreach (var c in enumCandidates) _output.WriteLine($"  {c}");

        foreach (var c in enumCandidates.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var pkgPath = c[..^".uasset".Length];
                var pkg = provider.LoadPackage(pkgPath);
                var enums = pkg.GetExports().OfType<UEnum>().ToList();
                _output.WriteLine($"  Loaded {c} → {enums.Count} enum(s)");
                foreach (var e in enums)
                {
                    _output.WriteLine($"    enum {e.Name} ({e.Names.Length} values):");
                    foreach (var (n, v) in e.Names)
                    {
                        _output.WriteLine($"      [{v}] {n.Text}");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! failed to load {c}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Survey door blueprints
        _output.WriteLine("");
        _output.WriteLine("=== SimpleDoor blueprint candidates ===");
        var doorPaths = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase) &&
                        (p.Contains("/Door", StringComparison.OrdinalIgnoreCase) ||
                         p.Contains("Door_", StringComparison.OrdinalIgnoreCase) ||
                         p.Contains("Doors/", StringComparison.OrdinalIgnoreCase)) &&
                        p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _output.WriteLine($"  total door-related uassets: {doorPaths.Count}");
        foreach (var d in doorPaths.Take(40)) _output.WriteLine($"  {d}");

        // Look specifically for SimpleDoor_ParentBP and SecurityDoor
        _output.WriteLine("");
        _output.WriteLine("=== Parent blueprints ===");
        foreach (var p in doorPaths.Where(p =>
            p.Contains("SimpleDoor", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("SecurityDoor", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("ParentBP", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  {p}");
        }
    }

    [Fact]
    public void Probe_DumpLiveSaveDoorClassesAndFlags()
    {
        var fixturePath = Fixtures.CascadeDir;
        if (fixturePath is null || !Directory.Exists(fixturePath))
        {
            _output.WriteLine("No fixture directory, skipping");
            return;
        }

        // Walk every WorldSave_*.sav we have, collect door IDs and flags, get unique classes.
        var savs = Directory.GetFiles(fixturePath, "WorldSave_*.sav");
        var classes = new HashSet<string>(StringComparer.Ordinal);
        var doorIdSamples = new List<string>();
        var allFlags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sav in savs)
        {
            try
            {
                var data = AbioticEditor.Core.WorldSaves.WorldSaveReader.ReadFromFile(sav);
                foreach (var door in data.Doors)
                {
                    var parsedClass = ExtractActorClassFromId(door.Id);
                    if (parsedClass is not null) classes.Add(parsedClass);
                    if (doorIdSamples.Count < 30) doorIdSamples.Add(door.Id);
                }
                foreach (var f in data.Flags) allFlags.Add(f);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! {sav}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine($"=== Door classes seen across {savs.Length} saves ===");
        foreach (var c in classes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"  {c}");
        }

        _output.WriteLine("");
        _output.WriteLine($"=== Sample door IDs ===");
        foreach (var id in doorIdSamples) _output.WriteLine($"  {id}");

        _output.WriteLine("");
        _output.WriteLine($"=== All flags ({allFlags.Count}) ===");
        foreach (var f in allFlags.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"  {f}");
        }
    }

    private static string? ExtractActorClassFromId(string id)
    {
        // /Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_0
        // -> SimpleDoor_ParentBP_C
        var lastDot = id.LastIndexOf('.');
        if (lastDot < 0 || lastDot == id.Length - 1) return null;
        var actor = id[(lastDot + 1)..];
        // strip trailing _<num>
        var lastUs = actor.LastIndexOf('_');
        if (lastUs > 0 && int.TryParse(actor[(lastUs + 1)..], out _))
        {
            return actor[..lastUs];
        }
        return actor;
    }

    [Fact]
    public void Probe_InspectSimpleDoorBlueprint()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return;

        var mappings = GameAssetProvider.FindConventionalMappings();
        if (mappings is null)
        {
            _output.WriteLine("No mappings — skipping detailed inspection");
            return;
        }

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        // Try to inspect a few promising door blueprints
        var candidates = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase) &&
                        p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                        (p.Contains("/Doors/", StringComparison.OrdinalIgnoreCase) ||
                         p.Contains("SimpleDoor", StringComparison.OrdinalIgnoreCase) ||
                         p.Contains("SecurityDoor", StringComparison.OrdinalIgnoreCase)))
            .Take(15).ToList();

        foreach (var c in candidates)
        {
            try
            {
                var pkgPath = c[..^".uasset".Length];
                var pkg = provider.LoadPackage(pkgPath);
                _output.WriteLine($"=== {pkgPath} ===");
                foreach (var e in pkg.GetExports())
                {
                    _output.WriteLine($"  export {e.Name} ({e.ExportType})");
                    var props = e.Properties;
                    foreach (var p in props.Take(20))
                    {
                        var v = p.Tag?.GenericValue?.ToString();
                        if (v is not null && v.Length > 60) v = v[..60] + "...";
                        _output.WriteLine($"    {p.Name}: {v}");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! {c}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
