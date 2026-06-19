using System.Globalization;
using System.Text.RegularExpressions;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Helpers for the opaque player identifier that names a player save
/// (<c>Player_&lt;id&gt;.sav</c>) and fills its top-level <c>SaveIdentifier</c> property.
/// On Steam this is a 17-digit SteamID64; on Game Pass / Microsoft Store / Epic and other
/// non-Steam copies it is some other account token. The editor treats the id as an opaque
/// string and never assumes it is numeric; <see cref="IsSteamId"/> is the single gate for
/// the Steam-only extras (persona lookup, achievements).
/// </summary>
public static partial class PlayerIdentifier
{
    /// <summary>
    /// Maximum identifier length we accept in a file name. A SteamID64 is 17 chars; this is
    /// generous headroom for a GUID-style or platform token without risking a path limit.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// True for a 17-digit public-range SteamID64 (same shape as the Steam
    /// <c>loginusers.vdf</c> persona regex). Used to gate Steam-only features; everything
    /// else is treated as a valid non-Steam id.
    /// </summary>
    public static bool IsSteamId(string? id)
        => id is not null && SteamIdRegex().IsMatch(id);

    /// <summary>
    /// True when <paramref name="id"/> is safe to use as the <c>Player_&lt;id&gt;.sav</c>
    /// file-name component: non-empty, within <see cref="MaxLength"/>, only letters, digits,
    /// <c>-</c>, <c>_</c> or <c>.</c> (no path separators, drive colons, wildcards or control
    /// chars), not a bare dot run and not a Windows reserved device name. This is the single
    /// place to relax if a real platform id needs other characters.
    /// </summary>
    public static bool IsSafeFileToken(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxLength) return false;
        if (!SafeTokenRegex().IsMatch(id)) return false;
        if (id.All(c => c == '.')) return false; // "." / ".." and friends
        return !ReservedNames.Contains(id);
    }

    /// <summary>
    /// Extracts the id from a <c>Player_&lt;id&gt;.sav</c> path. Returns false (and an empty
    /// id) when the file name does not carry the prefix. The id is returned verbatim, opaque -
    /// no numeric assumption. Consolidates the formerly-duplicated file-name parsers.
    /// </summary>
    public static bool TryParseFromPlayerFileName(string path, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrEmpty(path)) return false;
        var name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "Player_";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.Length <= prefix.Length)
        {
            return false;
        }
        id = name[prefix.Length..];
        return true;
    }

    /// <summary>
    /// Parses <paramref name="id"/> as a numeric SteamID64 for the Steam-only paths that need
    /// the number (achievements, vdf lookup). Returns false for any non-Steam id.
    /// </summary>
    public static bool TryParseSteamId(string? id, out ulong steamId)
    {
        steamId = 0;
        return IsSteamId(id)
            && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out steamId);
    }

    // Windows reserved device names (case-insensitive); a save named e.g. "CON" would be unusable.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    [GeneratedRegex(@"^7656119\d{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTokenRegex();
}
