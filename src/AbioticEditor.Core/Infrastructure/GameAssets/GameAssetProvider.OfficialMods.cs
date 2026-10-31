using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Assets;

public sealed partial class GameAssetProvider
{
    private sealed record ModRow(string Id, string Struct, FStructFallback Data, bool Override)
    {
        public (string Key, string Name, long? Id)? Mod { get; init; }
    }
    private Dictionary<string, List<ModRow>>? _officialRows;
    private readonly HashSet<UDataTable> _overlaidTables = [];

    /// <summary>Whether an official row supplies this table entry, including its localized text.</summary>
    internal bool IsOfficialRow(string tablePath, string id)
        => _officialRows?.GetValueOrDefault(tablePath)?.Any(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Which loaded mod a mounted asset comes from (<paramref name="change"/> says what it did), or
    /// null for a base-game asset. Works for both official mod.io archives and legacy <c>~mods</c> paks.
    /// </summary>
    public ModOrigin? ModOfAsset(string assetPath, ModChange change = ModChange.Added)
    {
        ThrowIfDisposed();
        if (_modFiles.Count == 0 || string.IsNullOrEmpty(assetPath)) return null;
        var path = assetPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ? assetPath : assetPath + ".uasset";
        return ArchiveModOf(path) is { } mod ? new ModOrigin(mod.Key, mod.Name, mod.Id, change) : null;
    }

    /// <summary>
    /// Which loaded mod supplies a row of a game data table, or null when the row is base game.
    /// When several mods touch one row, the one the game applies last is named. A row a mod added
    /// is <see cref="ModChange.Added"/>; a base row a mod overrode is <see cref="ModChange.Changed"/>.
    /// </summary>
    /// <param name="tablePath">Package path of the base table (no extension).</param>
    public ModOrigin? RowOrigin(string tablePath, string rowId)
    {
        ThrowIfDisposed();
        if (_modFiles.Count == 0 || !HasMappings) return null;
        using (ProviderLock())
        {
            _officialRows ??= ReadOfficialRows();
            if (!_officialRows.TryGetValue(StripExtension(tablePath), out var rows)) return null;
            var matching = rows.Where(row => row.Id.Equals(rowId, StringComparison.OrdinalIgnoreCase) && row.Mod is not null).ToArray();
            var winner = matching.LastOrDefault(row => row.Override) ?? matching.FirstOrDefault();
            return winner?.Mod is { } mod ? new ModOrigin(mod.Key, mod.Name, mod.Id, winner.Override ? ModChange.Changed : ModChange.Added) : null;
        }
    }

    /// <summary>
    /// Every data-table row the loaded official mods add or change, in the order the game applies
    /// them. Lets a host say in plain words what each mod brings (a new trader, new items, a changed
    /// trader's stock) without guessing from names. Empty with no mods or no mappings.
    /// </summary>
    public IReadOnlyList<ModRowContribution> ModContributions()
    {
        ThrowIfDisposed();
        if (_modFiles.Count == 0 || !HasMappings) return [];
        using (ProviderLock())
        {
            _officialRows ??= ReadOfficialRows();
            return _officialRows.SelectMany(table => table.Value
                    .Where(row => row.Mod is not null)
                    .Select(row => new ModRowContribution(
                        table.Key[(table.Key.LastIndexOf('/') + 1)..], row.Id,
                        new ModOrigin(row.Mod!.Value.Key, row.Mod.Value.Name, row.Mod.Value.Id, row.Override ? ModChange.Changed : ModChange.Added))))
                .DistinctBy(item => (item.Table, item.RowId, item.Origin.ModKey))
                .ToArray();
        }
    }

    /// <summary>
    /// How many of the base game's own files each mod replaces outright (a copy of a game file at
    /// the same path, the way older pak mods change things). Keyed by mod key.
    /// </summary>
    public IReadOnlyDictionary<string, int> ReplacedGameFileCounts()
    {
        ThrowIfDisposed();
        if (_modFiles.Count == 0) return new Dictionary<string, int>();
        return AssetPaths.Where(path => path.StartsWith("AbioticFactor/Content/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ArchiveModOf).OfType<(string Key, string Name, long? Id)>()
            .GroupBy(mod => mod.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private (string Key, string Name, long? Id)? ArchiveModOf(string assetPath)
    {
        if (!_provider.Files.TryGetValue(assetPath, out var file) || file is not CUE4Parse.UE4.VirtualFileSystem.VfsEntry entry
            || !_modArchives.TryGetValue(entry.Vfs.Path, out var archive)) return null;
        return (archive.Key, archive.Name, archive.Id);
    }

    private static string StripExtension(string path)
        => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ? path[..^7] : path;

    // Called under the provider lock. Only the in-memory table changes; pak files stay untouched.
    private void ApplyOfficialModRows(string tablePath, UDataTable table)
    {
        if (_modFiles.Count == 0 || !HasMappings || !_overlaidTables.Add(table)) return;
        _officialRows ??= ReadOfficialRows();
        tablePath = StripExtension(tablePath);
        // A composite table (CDT_*) is cooked with its parents' rows baked in, so mod rows aimed at
        // a parent (a new PhD in DT_PhDs, a rewritten job) must reach it too, in parent order,
        // exactly as the game rebuilds it. The table's own rows come last.
        foreach (var target in CompositeParents(table).Append(tablePath))
        {
            if (!_officialRows.TryGetValue(target, out var rows)) continue;
            foreach (var row in rows)
            {
                if (!string.Equals(row.Struct, table.RowStructName, StringComparison.Ordinal)) continue;
                var key = new FName(row.Id);
                if (row.Override || !table.RowMap.ContainsKey(key)) table.RowMap[key] = row.Data;
            }
        }
    }

    /// <summary>The package paths of a composite table's parents (none for an ordinary table).</summary>
    internal static IEnumerable<string> CompositeParents(UDataTable table)
    {
        if (table is not CUE4Parse.UE4.Assets.Exports.Engine.UCompositeDataTable
            || table.Properties.FirstOrDefault(p => p.Name.Text == "ParentTables")?.Tag?.GenericValue is not UScriptArray parents) yield break;
        foreach (var parent in parents.Properties)
        {
            if (parent.GenericValue is not FPackageIndex { ResolvedObject: { } resolved }) continue;
            var path = resolved.GetPathName().Split('.')[0];
            yield return path.StartsWith("/Game/", StringComparison.Ordinal) ? "AbioticFactor/Content/" + path[6..] : path.TrimStart('/');
        }
    }

    private Dictionary<string, List<ModRow>> ReadOfficialRows()
    {
        var result = new Dictionary<string, List<ModRow>>(StringComparer.OrdinalIgnoreCase);
        var assets = AssetPaths.Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)).ToArray();
        var baseTables = assets.Where(path => path.StartsWith("AbioticFactor/Content/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key!, group => group.Single()[..^7], StringComparer.OrdinalIgnoreCase);
        foreach (var configPath in OrderedOfficialConfigs(assets))
        {
            try
            {
                var config = _provider.LoadPackage(configPath).GetExports().FirstOrDefault(e => e.ExportType == "AbioticModConfiguration");
                if (config is null) continue;
                var prefix = config.Properties.FirstOrDefault(p => p.Name.Text == "ModPrefix")?.Tag?.GenericValue?.ToString();
                var root = configPath[..^"AbioticModConfig.uasset".Length];
                foreach (var path in assets.Where(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && IsModAsset(path)).Order(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        foreach (var export in _provider.LoadPackage(path).GetExports())
                        {
                            if (!export.ExportType.EndsWith("ModRowAsset", StringComparison.Ordinal)) continue;
                            var row = ReadOfficialRow(export, prefix);
                            if (row is null) continue;
                            row = row with { Mod = ArchiveModOf(path) };
                            // The official editor packages row assets under DataTables/<target table>/.
                            // Validate the target's native row struct when applying; never merge on
                            // row-struct identity alone (all three recipe tables share one struct).
                            var relative = path[root.Length..].Split('/');
                            var tableFolder = Array.FindIndex(relative, part => part.Equals("DataTables", StringComparison.OrdinalIgnoreCase));
                            if (tableFolder >= 0 && tableFolder + 1 < relative.Length
                                && baseTables.TryGetValue(relative[tableFolder + 1], out var target)) Add(target, row);
                            // Item rows are also collected into the game's global typed-handle table.
                            if (row.Struct == "AbioticInventoryItem" && baseTables.TryGetValue("ItemTable_Global", out var global)) Add(global, row);
                        }
                    }
                    catch (Exception ex) { Diagnostics.EditorLog.Warn("Mods", $"Could not inspect official mod asset '{path}'.", ex); }
                }
            }
            catch (Exception ex) { Diagnostics.EditorLog.Warn("Mods", $"Could not read official mod configuration '{configPath}'.", ex); }
        }
        return result;

        void Add(string target, ModRow row)
        {
            if (!result.TryGetValue(target, out var rows)) result[target] = rows = [];
            if (!rows.Contains(row)) rows.Add(row);
        }
    }

    private IEnumerable<string> OrderedOfficialConfigs(string[] assets)
    {
        var configs = new List<OfficialModOrder.Entry>();
        foreach (var path in assets.Where(path => path.EndsWith("/AbioticModConfig.uasset", StringComparison.OrdinalIgnoreCase) && IsModAsset(path)))
        {
            try
            {
                var archive = (CUE4Parse.UE4.VirtualFileSystem.VfsEntry)_provider.Files[path];
                var info = _modArchives.GetValueOrDefault(archive.Vfs.Path);
                var config = _provider.LoadPackage(path).GetExports().FirstOrDefault(e => e.ExportType == "AbioticModConfiguration");
                var after = config?.Properties.FirstOrDefault(p => p.Name.Text == "LoadAfter")?.Tag?.GenericValue is UScriptArray array
                    ? array.Properties.Select(p => p.GenericValue).OfType<long>().ToArray() : [];
                configs.Add(new(path, info.Id, info.Order, after));
            }
            catch (Exception ex) { Diagnostics.EditorLog.Warn("Mods", $"Could not read mod load order for '{path}'.", ex); }
        }
        var ordered = OfficialModOrder.Sort(configs);
        if (ordered.Count != configs.Count)
            Diagnostics.EditorLog.Warn("Mods", "Skipped official mod data with circular load-order dependencies.");
        return ordered.Select(entry => entry.Path);
    }

    private ModRow? ReadOfficialRow(UObject export, string? prefix)
    {
        var data = export.Properties.FirstOrDefault(p => p.Name.Text == "Row")?.Tag?.GenericValue;
        if ((data is FScriptStruct script ? script.StructType : data) is not FStructFallback row) return null;
        var name = export.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
        var replace = export.Properties.FirstOrDefault(p => p.Name.Text == "bOverrideExistingRow")?.Tag?.GenericValue is true;
        if (string.IsNullOrWhiteSpace(name) || name == "None" || !replace && (string.IsNullOrWhiteSpace(prefix) || prefix == "None")) return null;
        var type = _provider.MappingsContainer?.MappingsForGame?.Types.GetValueOrDefault(export.ExportType)?.Properties.Values
            .FirstOrDefault(p => p.Name == "Row")?.MappingType.StructType;
        return type is null ? null : new(replace ? name : prefix + "_" + name, type, row, replace);
    }
}
