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
    // Short class, friendly name, the EXACT wiki File name for its image (verified to resolve
    // via Special:FilePath), and whether the editor treats it as editable. The wiki's upload
    // convention is "Vehicle_-_<Name>.png"; the GATE cars and the ATV use names that don't
    // match their friendly label, so the file is curated rather than guessed. Null WikiImage =
    // no known wiki image (falls back to the looser guesses in WikiImageCandidates).
    //
    // Editable=false marks decorative / scripted / on-rails vehicles (the SnowGlobe Sleigh,
    // the Tram, the Minecart) whose drivable / wrecked / move state isn't meaningful to edit:
    // those are shown for reference only, with no toggles.
    private static readonly (string Short, string Friendly, string? WikiImage, bool Editable)[] Curated =
    {
        ("ABF_Vehicle_Forklift",     "Forklift",      "Vehicle_-_Forklift.png",          true),
        ("ABF_Vehicle_SecurityCart", "Security Cart", "Vehicle_-_GATE_Security_Cart.png", true),
        ("ABF_Vehicle_SUV",          "SUV",           "Vehicle_-_GATE_SUV.png",           true),
        ("ABF_Vehicle_Sleigh",       "Sleigh",        "Vehicle_-_Sleigh.png",            false),
        ("ABF_Vehicle_VOTV_ATV",     "VOTV ATV",      "Vehicle_-_ATV.png",                true),
        ("ABF_Vehicle_Tram",         "Tram",          "Vehicle_-_Tram.png",              false),
        ("ABF_Vehicle_GolfCart",     "Golf Cart",     null,                               true),
        ("ABF_Vehicle_Minecart",     "Minecart",      null,                              false),
        ("ABF_Vehicle_Boat",         "Boat",          null,                               true),
        ("ABF_Vehicle_Canoe",        "Canoe",         null,                               true),
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
        foreach (var (s, friendly, _, _) in Curated)
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
    /// Whether the editor offers edit controls (drivable / wrecked / move) for a vehicle.
    /// False for decorative / scripted / on-rails vehicles (Sleigh, Tram, Minecart), which
    /// are shown for reference only. Unknown classes default to editable.
    /// </summary>
    public static bool IsEditable(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        foreach (var (s, _, _, editable) in Curated)
        {
            if (string.Equals(s, shortClass, StringComparison.OrdinalIgnoreCase)) return editable;
        }
        return true;
    }

    /// <summary>
    /// Candidate wiki-image file names (tried in order by <see cref="WikiImageCache"/>) for a
    /// vehicle's appearance image. Covers the common upload conventions on the wiki.
    /// </summary>
    public static IReadOnlyList<string> WikiImageCandidates(string? classOrShort)
    {
        var shortClass = ShortOf(classOrShort);
        var list = new List<string>();

        // The curated, verified wiki File name first (correct for every shipped vehicle).
        foreach (var (s, _, wiki, _) in Curated)
        {
            if (string.Equals(s, shortClass, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(wiki)) list.Add(wiki!);
                break;
            }
        }

        // Looser guesses so a vehicle a future patch adds still has a chance to resolve.
        var friendly = FriendlyName(classOrShort);
        if (!string.IsNullOrEmpty(friendly))
        {
            list.Add($"Vehicle_-_{friendly!.Replace(' ', '_')}.png");
            list.Add($"{friendly}.png");
            list.Add($"Item Icon - {friendly}.png");
        }
        return list;
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
