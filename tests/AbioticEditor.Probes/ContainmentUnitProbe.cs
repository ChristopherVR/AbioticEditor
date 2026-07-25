using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for the CONTAINMENT tab rework: how a Leyak Containment Unit is
/// represented in the saves, how "unit X holds creature Y" is represented, and what an
/// EMPTY deployed unit looks like.
///
/// Dumps, across every fixture world:
///   1. LeyakContainmentIDs (the creature -> unit GUID map) with key/value CLR types.
///   2. Every DeployedObjectMap entry whose class name looks containment-related, with the
///      full struct property list so an empty unit can be told from an occupied one.
///   3. A cross-reference: which save file actually holds the GUID each containment id
///      points at (the tab assumes the Facility save, verify that).
/// </summary>
public class ContainmentUnitProbe
{
    private readonly ITestOutputHelper _output;

    public ContainmentUnitProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly string[] ClassNeedles =
    [
        "Leyak", "Containment", "Krasue", "Cage", "Trap", "Capture", "Anomaly",
    ];

    private static IEnumerable<(string Label, string Dir)> Worlds()
    {
        if (Fixtures.ServerWorldsDir is not null) yield return ("Server/Cascade", Fixtures.ServerWorldsDir);
        if (Fixtures.CascadeDir is not null) yield return ("Legacy/Cascade", Fixtures.CascadeDir);
        if (Fixtures.ClientSavedDir is not null)
        {
            foreach (var world in Directory.EnumerateDirectories(Fixtures.ClientSavedDir, "*", SearchOption.AllDirectories))
            {
                if (Directory.EnumerateFiles(world, "WorldSave_*.sav").Any())
                {
                    yield return ("Client/" + Path.GetFileName(world), world);
                }
            }
        }
    }

    private static SaveGame? TryLoad(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return SaveGame.LoadFrom(fs);
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public void Dump_LeyakContainmentIds_WithTypes()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ({dir}) ##########");
            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav").OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) { _output.WriteLine($"  !! unreadable {Path.GetFileName(sav)}"); continue; }

                var tag = save.Properties.FindByPrefix("LeyakContainmentIDs");
                if (tag is null) continue;

                _output.WriteLine($"=== {Path.GetFileName(sav)} ===");
                _output.WriteLine($"  tag name    : {tag.Name?.Value}");
                _output.WriteLine($"  property    : {tag.Property?.GetType().Name}");
                if (tag.Property is MapProperty mp)
                {
                    _output.WriteLine($"  key type    : {mp.KeyType}");
                    _output.WriteLine($"  value type  : {mp.ValueType}");
                    _output.WriteLine($"  entries     : {mp.Value?.Count ?? 0}");
                    foreach (var kv in mp.Value ?? [])
                    {
                        _output.WriteLine($"    key   [{kv.Key?.GetType().Name}] = {kv.Key?.Value}");
                        _output.WriteLine($"    value [{kv.Value?.GetType().Name}] = {kv.Value?.Value}  (valueCLR={kv.Value?.Value?.GetType().Name})");
                    }
                }
            }
        }
        _output.WriteLine("done");
    }

    [Fact]
    public void Dump_ContainmentLikeDeployables()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ##########");
            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav").OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) continue;

                if (save.Properties.FindByPrefix("DeployedObjectMap")?.Property is not MapProperty mp || mp.Value is null) continue;

                var printedHeader = false;
                foreach (var kv in mp.Value)
                {
                    if (kv.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

                    var classText = string.Join(
                        "|",
                        ps.Properties
                            .Where(p => p.Name?.Value?.StartsWith("Class", StringComparison.Ordinal) == true)
                            .Select(p => p.Property?.Value?.ToString() ?? ""));

                    if (!ClassNeedles.Any(n => classText.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;

                    if (!printedHeader)
                    {
                        _output.WriteLine($"=== {Path.GetFileName(sav)} (DeployedObjectMap: {mp.Value.Count}) ===");
                        printedHeader = true;
                    }

                    _output.WriteLine($"  --- key={kv.Key?.Value}");
                    foreach (var p in ps.Properties)
                    {
                        DumpProperty(p.Name?.Value, p.Property, "      ");
                    }
                }
            }
        }
        _output.WriteLine("done");
    }

    /// <summary>
    /// Prints EVERY distinct deployable class name per save with a count, so containment
    /// units can be spotted even if their class name does not contain an obvious needle.
    /// </summary>
    [Fact]
    public void Dump_AllDeployableClassNames()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ##########");
            var global = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav").OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) continue;
                if (save.Properties.FindByPrefix("DeployedObjectMap")?.Property is not MapProperty mp || mp.Value is null) continue;

                foreach (var kv in mp.Value)
                {
                    if (kv.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                    var cls = ps.Properties.FirstOrDefault(p => p.Name?.Value?.StartsWith("Class", StringComparison.Ordinal) == true)
                        ?.Property?.Value?.ToString() ?? "(no class)";
                    global.TryGetValue(cls, out var n);
                    global[cls] = n + 1;
                }
            }
            foreach (var (cls, n) in global) _output.WriteLine($"  {n,5}  {cls}");
        }
        _output.WriteLine("done");
    }

    /// <summary>
    /// Which save file physically holds the GUID that each LeyakContainmentIDs entry points at?
    /// (The UI assumes the Facility save; this checks every save in the world.)
    /// </summary>
    [Fact]
    public void CrossReference_ContainmentIdsToDeployables()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ##########");

            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav"))
            {
                var save = TryLoad(sav);
                if (save is null) continue;
                foreach (var pair in WorldSaveReader.ReadLeyakContainments(save))
                {
                    wanted[pair.Value] = $"{pair.Key} (from {Path.GetFileName(sav)})";
                }
            }
            _output.WriteLine($"  containment ids to locate: {wanted.Count}");
            foreach (var (id, who) in wanted) _output.WriteLine($"    {id} <- {who}");
            if (wanted.Count == 0) continue;

            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav").OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) continue;

                foreach (var mapName in new[] { "DeployedObjectMap", "NPCMap", "PetNPCMap", "InteractedActorMap" })
                {
                    if (save.Properties.FindByPrefix(mapName)?.Property is not MapProperty mp || mp.Value is null) continue;
                    foreach (var kv in mp.Value)
                    {
                        var key = kv.Key?.Value?.ToString();
                        if (key is null || !wanted.ContainsKey(key)) continue;
                        _output.WriteLine($"  HIT {Path.GetFileName(sav)} / {mapName} : {key}  ({wanted[key]})");
                        if (kv.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
                        {
                            foreach (var p in ps.Properties) DumpProperty(p.Name?.Value, p.Property, "        ");
                        }
                    }
                }
            }
        }
        _output.WriteLine("done");
    }

    /// <summary>
    /// Correlates each deployed containment unit with the creature the metadata save assigns to
    /// it, printing the unit's Generic1/2/3 slots alongside. This is the evidence that
    /// <c>Generic3</c> is the index into the blueprint's 2-entry <c>LeyakContainmentData</c>
    /// array (0 = Leyak, 1 = Krasue) and <c>Generic1</c> is the 0..100 stability level.
    /// </summary>
    [Fact]
    public void Correlate_UnitGenericSlotsWithAssignedCreature()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ##########");

            var creatureByUnit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav"))
            {
                var save = TryLoad(sav);
                if (save is null) continue;
                foreach (var pair in WorldSaveReader.ReadLeyakContainments(save)) creatureByUnit[pair.Value] = pair.Key;
            }

            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav").OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) continue;
                if (save.Properties.FindByPrefix("DeployedObjectMap")?.Property is not MapProperty mp || mp.Value is null) continue;

                foreach (var kv in mp.Value)
                {
                    if (kv.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                    var cls = ps.Properties.FirstOrDefault(p => p.Name?.Value?.StartsWith("Class", StringComparison.Ordinal) == true)
                        ?.Property?.Value?.ToString() ?? "";
                    if (!cls.Contains("Deployed_LeyakContainment", StringComparison.OrdinalIgnoreCase)) continue;

                    var id = kv.Key?.Value?.ToString() ?? "?";
                    var slots = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    if (ps.Properties.FindByPrefix("ChangableData_")?.Property is StructProperty cd
                        && cd.Value is PropertiesStruct cdps
                        && cdps.Properties.FindByPrefix("DynamicProperties_")?.Property is ArrayProperty ap
                        && ap.Value is not null)
                    {
                        for (var i = 0; i < ap.Value.Length; i++)
                        {
                            if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
                            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString() ?? "?";
                            slots[key] = eps.Properties.FindByPrefix("Value")?.Property?.Value is int ii ? ii : -999;
                        }
                    }
                    var creature = creatureByUnit.GetValueOrDefault(id, "(EMPTY - not in LeyakContainmentIDs)");
                    _output.WriteLine($"  {Path.GetFileName(sav)} {id} creature={creature}");
                    foreach (var (k, v) in slots) _output.WriteLine($"      {k} = {v}");
                }
            }
        }
        _output.WriteLine("done");
    }

    /// <summary>
    /// Histogram of <c>DynamicProperties_</c> {key -> observed values} per deployable class,
    /// across every fixture world save. Establishes how widely the generic slots
    /// (<c>Generic1/2/3</c>, <c>Portions</c>) are used and what values they take, which is the
    /// evidence for reading a containment unit's held-creature index out of one of them.
    /// </summary>
    [Fact]
    public void Dump_DeployableDynamicPropertyHistogram()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ##########");
            var byClass = new SortedDictionary<string, SortedDictionary<string, SortedSet<int>>>(StringComparer.Ordinal);

            foreach (var sav in Directory.EnumerateFiles(dir, "WorldSave_*.sav").OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) continue;
                if (save.Properties.FindByPrefix("DeployedObjectMap")?.Property is not MapProperty mp || mp.Value is null) continue;

                foreach (var kv in mp.Value)
                {
                    if (kv.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                    var cls = ps.Properties.FirstOrDefault(p => p.Name?.Value?.StartsWith("Class", StringComparison.Ordinal) == true)
                        ?.Property?.Value?.ToString() ?? "(no class)";
                    cls = cls[(cls.LastIndexOf('.') + 1)..];

                    if (ps.Properties.FindByPrefix("ChangableData_")?.Property is not StructProperty cd
                        || cd.Value is not PropertiesStruct cdps) continue;
                    if (cdps.Properties.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty ap
                        || ap.Value is null) continue;

                    for (var i = 0; i < ap.Value.Length; i++)
                    {
                        if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
                        var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString() ?? "?";
                        var value = eps.Properties.FindByPrefix("Value")?.Property?.Value switch
                        {
                            int ii => ii,
                            long ll => (int)ll,
                            _ => int.MinValue,
                        };
                        if (!byClass.TryGetValue(cls, out var keys)) byClass[cls] = keys = new(StringComparer.Ordinal);
                        if (!keys.TryGetValue(key, out var values)) keys[key] = values = [];
                        values.Add(value);
                    }
                }
            }

            foreach (var (cls, keys) in byClass)
            {
                _output.WriteLine($"  {cls}");
                foreach (var (key, values) in keys)
                {
                    _output.WriteLine($"      {key} = {{{string.Join(", ", values)}}}");
                }
            }
        }
        _output.WriteLine("done");
    }

    /// <summary>
    /// Hunts every fixture save (world AND player) for an inventory slot holding the packaged
    /// containment item (<c>Leyak_Containment</c>). A packaged unit is the only way to observe
    /// the state of a containment unit that is NOT currently deployed-and-occupied, so its
    /// DynamicProperties are the best available evidence for what an EMPTY unit looks like.
    /// </summary>
    [Fact]
    public void Hunt_PackagedContainmentItems()
    {
        foreach (var (label, dir) in Worlds())
        {
            _output.WriteLine($"########## {label} ##########");
            foreach (var sav in Directory.EnumerateFiles(dir, "*.sav", SearchOption.AllDirectories).OrderBy(p => p))
            {
                var save = TryLoad(sav);
                if (save is null) continue;
                var hits = 0;
                WalkForItem(save.Properties, "Leyak_Containment", Path.GetFileName(sav), ref hits, 0);
                if (hits > 0) _output.WriteLine($"  ({hits} hit(s) in {Path.GetFileName(sav)})");
            }
        }
        _output.WriteLine("done");
    }

    private void WalkForItem(IEnumerable<FPropertyTag>? props, string itemRow, string file, ref int hits, int depth)
    {
        if (props is null || depth > 8) return;
        foreach (var tag in props)
        {
            switch (tag.Property)
            {
                case StructProperty sp when sp.Value is PropertiesStruct ps:
                    if (ReportIfMatch(ps, itemRow, file, ref hits)) break;
                    WalkForItem(ps.Properties, itemRow, file, ref hits, depth + 1);
                    break;
                case ArrayProperty ap when ap.Value is { Length: > 0 }:
                    for (var i = 0; i < ap.Value.Length; i++)
                    {
                        if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
                        if (ReportIfMatch(eps, itemRow, file, ref hits)) continue;
                        WalkForItem(eps.Properties, itemRow, file, ref hits, depth + 1);
                    }
                    break;
                case MapProperty mp when mp.Value is not null:
                    foreach (var kv in mp.Value)
                    {
                        if (kv.Value is not StructProperty msp || msp.Value is not PropertiesStruct mps) continue;
                        if (ReportIfMatch(mps, itemRow, file, ref hits)) continue;
                        WalkForItem(mps.Properties, itemRow, file, ref hits, depth + 1);
                    }
                    break;
            }
        }
    }

    private bool ReportIfMatch(PropertiesStruct ps, string itemRow, string file, ref int hits)
    {
        var itemId = ps.Properties.FindByPrefix("ItemID")?.Property?.Value?.ToString();
        if (!string.Equals(itemId, itemRow, StringComparison.OrdinalIgnoreCase)) return false;
        hits++;
        _output.WriteLine($"  === {file}: slot with ItemID={itemId} ===");
        foreach (var p in ps.Properties) DumpProperty(p.Name?.Value, p.Property, "      ");
        return true;
    }

    private void DumpProperty(string? name, FProperty? prop, string indent)
    {
        if (prop is StructProperty sp && sp.Value is PropertiesStruct ps)
        {
            _output.WriteLine($"{indent}{name} (Struct:{sp.StructType})");
            foreach (var child in ps.Properties) DumpProperty(child.Name?.Value, child.Property, indent + "  ");
            return;
        }
        if (prop is ArrayProperty ap)
        {
            var len = ap.Value?.Length ?? 0;
            _output.WriteLine($"{indent}{name} (Array<{ap.ItemType}> x{len})");
            for (var i = 0; i < Math.Min(len, 8); i++)
            {
                var v = ap.Value!.GetValue(i);
                if (v is StructProperty esp && esp.Value is PropertiesStruct eps)
                {
                    _output.WriteLine($"{indent}  [{i}] struct");
                    foreach (var child in eps.Properties) DumpProperty(child.Name?.Value, child.Property, indent + "    ");
                }
                else
                {
                    _output.WriteLine($"{indent}  [{i}] {v}");
                }
            }
            return;
        }
        var s = prop?.Value?.ToString() ?? "(null)";
        if (s.Length > 200) s = s[..200] + "…";
        _output.WriteLine($"{indent}{name} ({prop?.GetType().Name}) = {s}");
    }
}
