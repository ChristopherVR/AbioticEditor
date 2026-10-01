using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Readers;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// The rows of <c>DT_Plants</c> (row struct <c>PlantData</c>): each crop's growth-stage meshes.
/// CUE4Parse reads this table as empty: the game's data table carries one more property than the
/// mappings describe (<c>bLoadFromJSON</c>, a one-byte flag), so the reader is one byte short and
/// takes the following zero (the object's "has GUID" flag) as the row count. The table's own bytes
/// are read here instead: the row block is found by trying each early position and keeping the
/// one where every row parses and the rows end at (or a few bytes before) the end of the export.
/// <para>
/// The bundled mappings also predate a field: the game's <c>PlantData</c> holds three item row
/// handles in a row (the mappings list two, <c>PlantItem</c> and <c>HarvestedItem</c>; the third
/// names the seed, e.g. <c>ItemTable_Pickups.seed_corn</c>), so every later field would be read
/// one place off. When the rows do not parse with the mappings as they
/// are, a corrected copy is registered under its own name (the shared <c>PlantData</c> entry is
/// left alone) and tried instead.
/// </para>
/// </summary>
internal static class PlantTable
{
    public const string Path = "AbioticFactor/Content/Blueprints/DataTables/DT_Plants.uasset";
    private const string RowStruct = "PlantData";
    private const int MaxHeaderBytes = 64;
    private const int MaxTrailingBytes = 8;

    public static IReadOnlyDictionary<string, FStructFallback> Read(IFileProvider provider)
    {
        var empty = new Dictionary<string, FStructFallback>(StringComparer.OrdinalIgnoreCase);
        if (!provider.TryLoadPackage(Path, out var package) || package is not IoPackage io || io.ExportMap.Length == 0) return empty;
        if (!provider.TrySaveAsset(Path, out var raw)) return empty;
        var exportBytes = io.ExportMap.Sum(e => (long)e.CookedSerialSize);
        var table = io.ExportMap[0];
        var start = raw.Length - exportBytes + (long)table.CookedSerialOffset;
        if (start < 0 || start + (long)table.CookedSerialSize > raw.Length) return empty;
        var data = raw.AsSpan((int)start, (int)table.CookedSerialSize).ToArray();

        foreach (var structName in new[] { RowStruct, Corrected(provider) })
        {
            if (structName is null) continue;
            for (var offset = 4; offset < Math.Min(MaxHeaderBytes, data.Length - 8); offset++)
            {
                if (TryRows(provider, io, data, offset, structName) is { } rows) return rows;
            }
        }
        return empty;
    }

    private const string CorrectedName = "AbioticEditor_PlantData";

    /// <summary>
    /// A copy of the mapped <c>PlantData</c> with one more row handle after <c>HarvestedItem</c>
    /// (later fields move up one index), registered once under <see cref="CorrectedName"/>.
    /// </summary>
    private static string? Corrected(IFileProvider provider)
    {
        var types = provider.MappingsForGame?.Types;
        if (types is null || !types.TryGetValue(RowStruct, out var original)) return null;
        if (types.ContainsKey(CorrectedName)) return CorrectedName;
        // Keys are field positions (a PropertyInfo's own Index is its array element, not its position).
        var harvested = original.Properties.FirstOrDefault(kv => kv.Value.Name == "HarvestedItem");
        if (harvested.Value is null) return null;
        var properties = new Dictionary<int, PropertyInfo>();
        foreach (var (position, property) in original.Properties)
        {
            properties[position > harvested.Key ? position + 1 : position] = property;
        }
        properties[harvested.Key + 1] = new PropertyInfo(0, "SeedItem", harvested.Value.MappingType);
        types[CorrectedName] = new Struct(original.Context, CorrectedName, original.SuperType, properties, original.PropertyCount + 1);
        return CorrectedName;
    }

    private static Dictionary<string, FStructFallback>? TryRows(IFileProvider provider, IPackage package, byte[] data, int offset, string structName)
    {
        var count = BitConverter.ToInt32(data, offset);
        if (count is <= 0 or > 4096) return null;
        try
        {
            var archive = new FAssetArchive(new FByteArchive("DT_Plants", data, provider.Versions), package) { Position = offset + 4 };
            var rows = new Dictionary<string, FStructFallback>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                var name = archive.ReadFName();
                if (string.IsNullOrEmpty(name.Text) || name.Text == "None") return null;
                rows[name.Text] = new FStructFallback(archive, structName);
            }
            // A few bytes may follow the last row (DT_Plants has four).
            var left = data.Length - archive.Position;
            return left is >= 0 and <= MaxTrailingBytes ? rows : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
