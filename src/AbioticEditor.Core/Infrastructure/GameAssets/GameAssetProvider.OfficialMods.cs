using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Assets;

public sealed partial class GameAssetProvider
{
    private sealed record ModRow(string Id, string Struct, FStructFallback Data, bool Override);
    private Dictionary<string, List<ModRow>>? _officialRows;
    private readonly HashSet<UDataTable> _overlaidTables = [];

    /// <summary>Whether an official row supplies this table entry, including its localized text.</summary>
    internal bool IsOfficialRow(string tablePath, string id)
        => _officialRows?.GetValueOrDefault(tablePath)?.Any(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == true;

    // Called under the provider lock. Only the in-memory table changes; pak files stay untouched.
    private void ApplyOfficialModRows(string tablePath, UDataTable table)
    {
        if (_modFiles.Count == 0 || !HasMappings || !_overlaidTables.Add(table)) return;
        _officialRows ??= ReadOfficialRows();
        tablePath = tablePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ? tablePath[..^7] : tablePath;
        if (!_officialRows.TryGetValue(tablePath, out var rows)) return;
        foreach (var row in rows)
        {
            if (!string.Equals(row.Struct, table.RowStructName, StringComparison.Ordinal)) continue;
            var key = new FName(row.Id);
            if (row.Override || !table.RowMap.ContainsKey(key)) table.RowMap[key] = row.Data;
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
