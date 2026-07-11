namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Turns a region/level token (parsed from a world-save file name or an actor's
/// <c>SoftObjectPath</c>) into a friendly in-game area name, e.g. <c>Facility_MFWest</c> →
/// "Manufacturing West", <c>WorldSave_Facility_Office1</c> → "The Office Sector".
///
/// <para>This is a best-effort label, not an authoritative coordinate lookup: the game does
/// not store a coordinate-to-area mapping in the save, so the area is inferred from the level
/// the actor/save belongs to. Tokens we recognise get a curated name (aligned with the
/// teleporter region names in <see cref="Features.TeleporterTagCatalog"/>); everything else is
/// prettified (strip the <c>Facility_</c> prefix, split underscores) so a new level still reads
/// sensibly rather than showing a raw path.</para>
/// </summary>
public static class WorldAreaCatalog
{
    // Curated names for the Facility sub-levels we are confident about. Keyed by the token with
    // the leading "Facility_" stripped; matched by prefix so "Dam_Central" resolves via "Dam".
    private static readonly (string Prefix, string Name)[] FacilityAreas =
    {
        ("Office", "The Office Sector"),
        ("MFWest", "Manufacturing West"),
        ("MFMines", "The Mines"),
        ("MFFoundry", "Manufacturing - Foundry"),
        ("MFHQ", "Manufacturing - Headquarters"),
        ("MFMaggot", "Manufacturing - Maggot Pit"),
        ("MF", "Manufacturing"),
        ("Labs", "Cascade Laboratories"),
        ("Security", "Security Sector"),
        ("Dam", "The Dam"),
        ("Hydroplant", "Hydroplant"),
        ("Reservoir", "Reservoir"),
        ("Pens", "Containment Pens"),
        ("Containment", "Containment"),
        ("Parking", "Parking Garage"),
        ("DF_RadWaste", "Radioactive Waste"),
        ("DF", "Deep Facility"),
        ("Reactors", "The Reactors"),
        ("Residence", "Residence Sector"),
    };

    /// <summary>
    /// The friendly area name for a level token (e.g. <c>Facility_MFWest</c>, <c>V_Train</c>),
    /// or null when <paramref name="token"/> is empty.
    /// </summary>
    public static string? FriendlyName(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var t = token.Trim();

        // The aggregate persistent level.
        if (string.Equals(t, "Facility", StringComparison.OrdinalIgnoreCase))
        {
            return "The Facility";
        }
        if (string.Equals(t, "MetaData", StringComparison.OrdinalIgnoreCase))
        {
            return "World (metadata)";
        }

        if (t.StartsWith("Facility_", StringComparison.OrdinalIgnoreCase))
        {
            var sub = t["Facility_".Length..];
            foreach (var (prefix, name) in FacilityAreas)
            {
                if (sub.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
            return Prettify(sub);
        }

        // Alternate/anteverse worlds (V_*, H_*, Vignette_*) - prettify the remainder.
        if (t.StartsWith("V_", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("H_", StringComparison.OrdinalIgnoreCase))
        {
            return Prettify(t[2..]);
        }
        if (t.StartsWith("Vignette_", StringComparison.OrdinalIgnoreCase))
        {
            return Prettify(t["Vignette_".Length..]);
        }

        return Prettify(t);
    }

    /// <summary>
    /// The friendly area name for a world-save file name (e.g. <c>WorldSave_Facility_Office1</c>
    /// or a full path), or null when it isn't a recognisable world save.
    /// </summary>
    public static string? FriendlyNameFromSaveFile(string? fileNameOrPath)
    {
        var token = TokenFromSaveFile(fileNameOrPath);
        return token is null ? null : FriendlyName(token);
    }

    /// <summary>
    /// The level token of a world-save file name: <c>WorldSave_Facility_Office1.sav</c> →
    /// <c>Facility_Office1</c>. Returns null when the name isn't a <c>WorldSave_*</c> file.
    /// </summary>
    public static string? TokenFromSaveFile(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return null;
        }
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath.Trim());
        const string prefix = "WorldSave_";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var token = name[prefix.Length..];
        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// The level token embedded in an actor object path:
    /// <c>/Game/Maps/Facility_MFWest.Facility_MFWest:PersistentLevel.Forklift_C_3</c> →
    /// <c>Facility_MFWest</c>. Returns null when no <c>/Maps/</c> segment is present.
    /// </summary>
    public static string? TokenFromActorPath(string? actorPath)
    {
        if (string.IsNullOrWhiteSpace(actorPath))
        {
            return null;
        }
        const string marker = "/Maps/";
        var idx = actorPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }
        var rest = actorPath[(idx + marker.Length)..];
        // The token ends at the first of '.', ':' or '/'.
        var end = rest.Length;
        foreach (var ch in new[] { '.', ':', '/' })
        {
            var p = rest.IndexOf(ch);
            if (p >= 0 && p < end)
            {
                end = p;
            }
        }
        var token = rest[..end];
        return token.Length == 0 ? null : token;
    }

    /// <summary>
    /// The friendly area name for an actor path, falling back to a save-file token when the path
    /// carries no <c>/Maps/</c> segment (e.g. a GUID-keyed entry).
    /// </summary>
    public static string? FriendlyNameFor(string? actorPath, string? fallbackSaveFile)
    {
        var token = TokenFromActorPath(actorPath) ?? TokenFromSaveFile(fallbackSaveFile);
        return token is null ? null : FriendlyName(token);
    }

    private static string Prettify(string token)
    {
        var spaced = token.Replace('_', ' ').Trim();
        return spaced.Length == 0 ? token : spaced;
    }
}
