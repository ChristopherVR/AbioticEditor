using System.Globalization;

namespace AbioticEditor.Core.Compare;

/// <summary>
/// What kind of thing a single difference is. Only <see cref="Gameplay"/> counts as a
/// "real" change; the rest are differences you'd expect any two distinct saves to have
/// (a different account's id, a later play session's clock, per-instance object handles,
/// the player standing somewhere else) and are usually noise when you're hunting for what
/// actually changed.
/// </summary>
public enum SaveDiffCategory
{
    /// <summary>A meaningful change: money, skills, items, flags, story progress, etc.</summary>
    Gameplay,

    /// <summary>Account identity - the stored SteamID / SaveIdentifier.</summary>
    Identity,

    /// <summary>Clock / playtime counters (minutes played, current day, time of day).</summary>
    Playtime,

    /// <summary>A wall-clock timestamp (last-played, save time).</summary>
    Timestamp,

    /// <summary>A per-instance object handle (AssetID / level GUID) - changes every spawn.</summary>
    InstanceId,

    /// <summary>A world position or rotation - drifts as the player moves.</summary>
    Position,
}

/// <summary>
/// Assigns a <see cref="SaveDiffCategory"/> to a difference from its (normalized) path, the
/// property type and the values. Heuristic by design - it errs toward leaving things as
/// <see cref="SaveDiffCategory.Gameplay"/> so nothing meaningful is hidden by accident.
/// </summary>
public static class SaveDiffClassifier
{
    public static bool IsNoise(this SaveDiffCategory category) => category != SaveDiffCategory.Gameplay;

    public static SaveDiffCategory Classify(string path, string type, string? left, string? right)
    {
        // The final path segment carries the property name; strip any array/map suffix.
        var leaf = LeafName(path);

        // --- Identity: the stored account id. ---
        if (leaf.Equals("SaveIdentifier", StringComparison.OrdinalIgnoreCase))
        {
            return SaveDiffCategory.Identity;
        }

        // --- Playtime / clock counters. ---
        if (NameIsAny(leaf,
                "MinutesPassed", "MinutesPlayed", "SecondsPlayed", "TimeOfDaySeconds",
                "CurrentDay", "LastAssaultDay", "LastWeatherDay", "LastPowerLeechDay")
            || leaf.Contains("PlayTime", StringComparison.OrdinalIgnoreCase)
            || leaf.Contains("PlayedTime", StringComparison.OrdinalIgnoreCase))
        {
            return SaveDiffCategory.Playtime;
        }

        // --- Timestamps. ---
        if (NameIsAny(leaf, "LastPlayed", "Timestamp", "SaveTime", "SaveDate", "LastSaveTime")
            || string.Equals(type, "DateTime", StringComparison.OrdinalIgnoreCase))
        {
            return SaveDiffCategory.Timestamp;
        }

        // --- Per-instance object handles. The value shape (a 32-hex / dashed GUID) is the
        // strongest signal: every spawned item carries a unique AssetID that differs between
        // any two saves but means nothing on its own. ---
        if (leaf.Equals("AssetID", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith("GUID", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith("Guid", StringComparison.Ordinal)
            || BothLookLikeGuid(left, right))
        {
            return SaveDiffCategory.InstanceId;
        }

        // --- Positions / rotations. ---
        if (NameContainsAny(leaf, "Location", "Rotation", "Translation", "Transform", "Velocity")
            || BothLookLikeVector(left, right))
        {
            return SaveDiffCategory.Position;
        }

        return SaveDiffCategory.Gameplay;
    }

    private static string LeafName(string path)
    {
        // Drop array "[i]" / map "{key}" suffixes, then take the segment after the last dot.
        var end = path.Length;
        // Trim a trailing [..] or {..}.
        if (end > 0 && (path[end - 1] == ']' || path[end - 1] == '}'))
        {
            var open = path.LastIndexOfAny(new[] { '[', '{' });
            if (open >= 0) end = open;
        }
        var dot = path.LastIndexOf('.', Math.Max(0, end - 1));
        var start = dot >= 0 ? dot + 1 : 0;
        return path.Substring(start, end - start);
    }

    private static bool NameIsAny(string leaf, params string[] names)
    {
        foreach (var n in names)
        {
            if (leaf.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool NameContainsAny(string leaf, params string[] fragments)
    {
        foreach (var f in fragments)
        {
            if (leaf.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool BothLookLikeGuid(string? a, string? b)
    {
        // For a changed leaf both sides are present; for added/removed only one is.
        var present = (a is not null && a.Length > 0) || (b is not null && b.Length > 0);
        if (!present) return false;
        return (a is null || a.Length == 0 || LooksLikeGuid(a))
            && (b is null || b.Length == 0 || LooksLikeGuid(b))
            && (LooksLikeGuid(a) || LooksLikeGuid(b));
    }

    private static bool LooksLikeGuid(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        // Teleporter handles carry a trailing comma; tolerate trailing punctuation.
        var v = s.TrimEnd(',', ' ');

        // 32 raw hex chars (the AssetID form).
        if (v.Length == 32 && IsHex(v)) return true;

        // 8-4-4-4-12 dashed form.
        if (v.Length == 36 && v[8] == '-' && v[13] == '-' && v[18] == '-' && v[23] == '-')
        {
            return IsHex(v.Replace("-", string.Empty));
        }
        return false;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return s.Length > 0;
    }

    private static bool BothLookLikeVector(string? a, string? b)
    {
        var hasA = !string.IsNullOrEmpty(a);
        var hasB = !string.IsNullOrEmpty(b);
        if (!hasA && !hasB) return false;
        return (!hasA || LooksLikeVector(a!)) && (!hasB || LooksLikeVector(b!));
    }

    /// <summary>Three (or four) space-separated numbers, e.g. a Vector or Rotator rendering.</summary>
    private static bool LooksLikeVector(string s)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 3 or > 4) return false;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return false;
        }
        return true;
    }
}
