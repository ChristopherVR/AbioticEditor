using System.Globalization;
using System.Text;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

/// <summary>
/// Shared helpers for the base-editing tests: locating real fixture objects, a deep per-entry
/// fingerprint of a whole save, and a diff of two fingerprints so a test can assert EXACTLY which
/// entries changed and that every other entry of every other map is identical.
/// </summary>
internal static class BaseEditTestSupport
{
    private static readonly Lazy<byte[]?> ServerFacilityBytes = new(() =>
        Fixtures.ServerWorldsDir is { } d && File.Exists(Path.Combine(d, "WorldSave_Facility.sav"))
            ? File.ReadAllBytes(Path.Combine(d, "WorldSave_Facility.sav"))
            : null);

    /// <summary>True when the dedicated-server Facility save (power sockets, teleporters, beds) is present.</summary>
    public static bool HasServerFacility => ServerFacilityBytes.Value is not null;

    /// <summary>The raw bytes of the server Facility fixture.</summary>
    public static byte[] OriginalBytes => ServerFacilityBytes.Value!;

    /// <summary>A freshly parsed copy of the server Facility save.</summary>
    public static WorldSaveData Load()
    {
        using var ms = new MemoryStream(OriginalBytes, writable: false);
        return WorldSaveReader.ReadFromStream(ms);
    }

    public static byte[] Serialize(WorldSaveData data)
    {
        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Deterministic id source: 32-hex ids that cannot collide with real GUIDs.</summary>
    public static Func<string> Counter(char prefix = 'E')
    {
        var n = 0;
        return () => string.Create(CultureInfo.InvariantCulture, $"{new string(prefix, 24)}{++n:X8}");
    }

    // ---------- fingerprints ----------

    /// <summary>map name to (entry key to deep value fingerprint), for every top-level map of the save.</summary>
    public static Dictionary<string, Dictionary<string, string>> Fingerprint(SaveGame save)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var top in save.Properties ?? [])
        {
            var name = top.Name?.Value ?? "?";
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            if (top.Property is MapProperty { Value: { } pairs })
            {
                var n = 0;
                foreach (var kv in pairs)
                {
                    var key = WorldSaveReader_Key(kv.Key) ?? "#" + n.ToString(CultureInfo.InvariantCulture);
                    entries[key] = Fp(kv.Value);
                    n++;
                }
            }
            else
            {
                entries["(value)"] = Fp(top);
            }
            result[name] = entries;
        }
        return result;
    }

    private static string? WorldSaveReader_Key(FProperty key)
        => key.Value switch { FString fs => fs.Value, string s => s, var v => v?.ToString() };

    /// <summary>Entries that differ between two fingerprints: added, removed, changed (map/key).</summary>
    public static (List<string> Added, List<string> Removed, List<string> Changed) Diff(
        Dictionary<string, Dictionary<string, string>> before, Dictionary<string, Dictionary<string, string>> after)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        foreach (var map in before.Keys.Union(after.Keys, StringComparer.Ordinal))
        {
            var b = before.GetValueOrDefault(map) ?? [];
            var a = after.GetValueOrDefault(map) ?? [];
            foreach (var k in a.Keys.Where(k => !b.ContainsKey(k))) added.Add(map + "/" + k);
            foreach (var k in b.Keys.Where(k => !a.ContainsKey(k))) removed.Add(map + "/" + k);
            foreach (var k in a.Keys.Where(k => b.ContainsKey(k) && b[k] != a[k])) changed.Add(map + "/" + k);
        }
        return (added, removed, changed);
    }

    public static string Fp(object? n)
    {
        var sb = new StringBuilder();
        Write(sb, n);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? n)
    {
        switch (n)
        {
            case null:
                sb.Append("null");
                break;
            case FPropertyTag t:
                sb.Append(t.Name?.Value).Append(':').Append(t.Type?.Name?.Value).Append('=');
                Write(sb, t.Property);
                sb.Append(';');
                break;
            case StructProperty sp:
                sb.Append('{');
                Write(sb, sp.Value);
                sb.Append('}');
                break;
            case PropertiesStruct ps:
                foreach (var t in ps.Properties) Write(sb, t);
                break;
            case GameplayTagContainerStruct g:
                sb.Append("tags[").AppendJoin(',', g.Tags.Select(x => x?.Value)).Append(']');
                break;
            case ArrayProperty ap:
                sb.Append('[');
                if (ap.Value is { } arr)
                {
                    foreach (var i in arr)
                    {
                        Write(sb, i);
                        sb.Append(',');
                    }
                }
                sb.Append(']');
                break;
            case MapProperty mp:
                sb.Append("map(").Append((mp.Value?.Count ?? 0).ToString(CultureInfo.InvariantCulture)).Append(')');
                break;
            case FProperty p:
                sb.Append(p.GetType().Name).Append(':').Append(Convert.ToString(p.Value, CultureInfo.InvariantCulture));
                break;
            case FString fs:
                sb.Append(fs.Value);
                break;
            default:
                if (n.GetType().Name == "SoftObjectPathStruct"
                    && n.GetType().GetProperty("Value")?.GetValue(n) is UeSaveGame.DataTypes.SoftObjectPath sop)
                {
                    sb.Append("soft:").Append(sop.PackageName?.Value).Append('|').Append(sop.AssetName?.Value)
                        .Append('|').Append(sop.SubPathString?.Value);
                }
                else
                {
                    sb.Append(Convert.ToString(n, CultureInfo.InvariantCulture));
                }
                break;
        }
    }

    // ---------- fixture object finders ----------

    public static IEnumerable<PlacedObjectSummary> PlayerBuilt(WorldSaveData data)
        => PlacedObjectCensus.Build(data).Objects!.Where(o => o.DeployedByPlayer == true && o.Key.Length == 32);

    /// <summary>Socket records as (id, owner, plugged) from the raw map.</summary>
    public static List<(string Id, string? Owner, string? Plugged)> Sockets(WorldSaveData data)
        => WorldMapAccessor.Entries(data.Raw, "PowerSocketMap")
            .Select(e =>
            {
                var plugged = e.Props.GetString("PluggedInDeviceAssetID_");
                return (e.Key, PlacedGroupReferenceAnalyzer.OwnerKeyOf(e.Props.GetString("PowerSocket_") ?? e.Key),
                    string.IsNullOrEmpty(plugged) || plugged == "-1" ? null : plugged);
            }).ToList();

    public static IList<FPropertyTag> Entry(WorldSaveData data, string key)
        => WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", key)!;

    public static string SocketPlugged(WorldSaveData data, string socketId)
        => WorldMapAccessor.FindEntry(data.Raw, "PowerSocketMap", socketId)!.GetString("PluggedInDeviceAssetID_") ?? string.Empty;
}
