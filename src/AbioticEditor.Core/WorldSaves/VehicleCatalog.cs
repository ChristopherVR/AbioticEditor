using AbioticEditor.Core.Assets;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Reference data for vehicles: friendly names for the <c>ABF_Vehicle_*</c> blueprint
/// classes and candidate wiki-image file names so the editor can show "how it looks"
/// (the same abioticfactor.wiki.gg image mechanism the fish codex uses).
///
/// Curated entries cover the known vehicles; <see cref="FriendlyName"/> prettifies any
/// class not in the table, so vehicles a future patch adds still render sensibly.
/// </summary>
public static class VehicleCatalog
{
    private static readonly (string Short, string Friendly)[] Curated =
    {
        ("ABF_Vehicle_Forklift", "Forklift"),
        ("ABF_Vehicle_SecurityCart", "Security Cart"),
        ("ABF_Vehicle_GolfCart", "Golf Cart"),
        ("ABF_Vehicle_Minecart", "Minecart"),
        ("ABF_Vehicle_Boat", "Boat"),
        ("ABF_Vehicle_Canoe", "Canoe"),
    };

    /// <summary>Short class-name (without <c>_C</c>) for a full path or short string.</summary>
    public static string ShortOf(string? classOrShort)
    {
        if (string.IsNullOrEmpty(classOrShort)) return string.Empty;
        var tail = classOrShort[(classOrShort.LastIndexOf('.') + 1)..];
        return tail.EndsWith("_C", StringComparison.Ordinal) ? tail[..^2] : tail;
    }

    /// <summary>Friendly name for a vehicle class (curated, else prettified), null only for empty input.</summary>
    public static string? FriendlyName(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        if (shortClass.Length == 0) return null;
        foreach (var (s, friendly) in Curated)
        {
            if (string.Equals(s, shortClass, StringComparison.OrdinalIgnoreCase)) return friendly;
        }
        var name = shortClass;
        foreach (var prefix in new[] { "ABF_Vehicle_", "Vehicle_", "ABF_" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)) { name = name[prefix.Length..]; break; }
        }
        return name.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// Candidate wiki-image file names (tried in order by <see cref="WikiImageCache"/>) for a
    /// vehicle's appearance image. Covers the common upload conventions on the wiki.
    /// </summary>
    public static IReadOnlyList<string> WikiImageCandidates(string? classOrShort)
    {
        var friendly = FriendlyName(classOrShort);
        if (string.IsNullOrEmpty(friendly)) return Array.Empty<string>();
        return new[]
        {
            $"{friendly}.png",
            $"Item Icon - {friendly}.png",
            $"{friendly} (vehicle).png",
            $"Itemicon_{friendly.Replace(" ", string.Empty)}.png",
        };
    }

    /// <summary>
    /// Every <c>ABF_Vehicle_*</c> class the mounted paks expose, as (friendly, classPath).
    /// Empty when no provider / enumeration fails. Useful for diagnostics and future tooling.
    /// </summary>
    public static IReadOnlyList<(string Friendly, string ClassPath)> EnumerateFromPaks(GameAssetProvider? provider)
    {
        if (provider is null) return Array.Empty<(string, string)>();
        var result = new List<(string, string)>();
        try
        {
            foreach (var key in provider.AssetPaths)
            {
                if (key.IndexOf("/Vehicles/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(key);
                if (!name.StartsWith("ABF_Vehicle_", StringComparison.OrdinalIgnoreCase)) continue;

                var pkg = key[..^".uasset".Length];
                var idx = pkg.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
                pkg = idx >= 0 ? "/Game" + pkg[(idx + "/Content".Length)..] : pkg;
                result.Add((FriendlyName(name) ?? name, $"{pkg}.{name}_C"));
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("VehicleCatalog", $"Pak enumeration failed: {ex.Message}");
        }
        return result;
    }
}
