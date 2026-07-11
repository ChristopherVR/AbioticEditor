using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.Steam;

/// <summary>One achievement definition + a player's unlock state.</summary>
public sealed record AchievementState(
    string ApiName,
    string DisplayName,
    string? Description,
    bool Hidden,
    bool Unlocked,
    string? IconUrl = null,
    string? IconGrayUrl = null);

/// <summary>
/// Read-only view of a player's Steam achievements for Abiotic Factor, parsed from the
/// Steam client's local stats cache:
/// <c>&lt;steam&gt;/appcache/stats/UserGameStatsSchema_&lt;appid&gt;.bin</c> (definitions) and
/// <c>&lt;steam&gt;/appcache/stats/UserGameStats_&lt;accountid&gt;_&lt;appid&gt;.bin</c> (state).
/// Both are Valve binary-KeyValues files. We never write these - Steam owns them.
/// </summary>
public static class SteamAchievements
{
    /// <summary>Abiotic Factor's Steam app id.</summary>
    public const int AppId = 427410;

    private const long SteamId64Base = 76561197960265728L;

    /// <summary>
    /// Loads achievements for the player with the given SteamID64 (the number in
    /// <c>Player_&lt;steamid64&gt;.sav</c>). Returns null when Steam, the schema, or the
    /// player's stats file can't be found.
    /// </summary>
    /// <summary>
    /// Loads achievements for an opaque owner id. Achievements are a Steam feature, so this
    /// returns null for any non-Steam id (Game Pass / Epic) - the single gate via
    /// <see cref="PlayerIdentifier.IsSteamId"/>.
    /// </summary>
    public static IReadOnlyList<AchievementState>? LoadFor(string id)
        => PlayerIdentifier.TryParseSteamId(id, out var steamId64)
            ? LoadFor((long)steamId64)
            : null;

    public static IReadOnlyList<AchievementState>? LoadFor(long steamId64)
    {
        var steam = AfInstallLocator.FindSteamPath();
        if (steam is null) return null;

        var statsDir = Path.Combine(steam, "appcache", "stats");
        var schemaPath = Path.Combine(statsDir, $"UserGameStatsSchema_{AppId}.bin");
        if (!File.Exists(schemaPath)) return null;

        var accountId = steamId64 - SteamId64Base;
        var statsPath = Path.Combine(statsDir, $"UserGameStats_{accountId}_{AppId}.bin");

        try
        {
            var schema = BinaryKeyValues.Parse(File.ReadAllBytes(schemaPath));
            var stats = File.Exists(statsPath)
                ? BinaryKeyValues.Parse(File.ReadAllBytes(statsPath))
                : null;

            return ExtractAchievements(schema, stats);
        }
        catch
        {
            return null;
        }
    }

    private static List<AchievementState> ExtractAchievements(KvNode schema, KvNode? stats)
    {
        var result = new List<AchievementState>();

        // Schema layout: <appid> -> "stats" -> <statId> -> { "type"=4, "bits" -> <bit> ->
        // { "name", "display" -> { "name", "desc", "hidden" } } }. Display values are
        // usually language dicts ("english" -> text).
        var statsNode = schema.FindPath(
                AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "stats")
            ?? schema.Find("stats");
        if (statsNode is null) return result;

        foreach (var stat in statsNode.Children)
        {
            // The type field is the string "ACHIEVEMENTS" in current schemas (older
            // ones used the numeric enum value 4).
            var typeNode = stat.Find("type");
            var isAchievements = typeNode?.AsString() == "ACHIEVEMENTS" || typeNode?.AsInt() == 4;
            var bits = stat.Find("bits");
            if (!isAchievements || bits is null) continue;

            // The unlock state for this stat block: an int whose bit N is achievement N.
            var unlockedBits = ReadUnlockedBits(stats, stat.Key);

            foreach (var bit in bits.Children)
            {
                if (!int.TryParse(bit.Key, out var bitIndex)) continue;
                var apiName = bit.Find("name")?.AsString();
                if (apiName is null) continue;

                var display = bit.Find("display");
                var displayName = LocalizedText(display?.Find("name")) ?? apiName;
                var description = LocalizedText(display?.Find("desc"));
                var hidden = display?.Find("hidden")?.AsInt() == 1;
                var unlocked = (unlockedBits >> bitIndex & 1) == 1;

                // The schema carries CDN image hashes - same icons Steam shows.
                var icon = IconUrlFor(display?.Find("icon")?.AsString());
                var iconGray = IconUrlFor(display?.Find("icon_gray")?.AsString());

                result.Add(new AchievementState(apiName, displayName, description, hidden, unlocked, icon, iconGray));
            }
        }
        return result;
    }

    private static long ReadUnlockedBits(KvNode? stats, string statId)
    {
        if (stats is null) return 0;
        // UserGameStats layout: cache -> <statId> -> data (int bitfield; bit N =
        // achievement N of that stat block). Negative values are just the signed view
        // of a full bitfield - reinterpret as uint.
        var node = stats.FindPath("cache", statId, "data")
                ?? stats.FindDeep(statId)?.Find("data");
        var v = node?.AsLong() ?? 0;
        return v < 0 ? (uint)v : v;
    }

    private static string? IconUrlFor(string? hash)
        => string.IsNullOrEmpty(hash)
            ? null
            : $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{AppId}/{hash}";

    private static string? LocalizedText(KvNode? node)
    {
        if (node is null) return null;
        if (node.Value is string s) return s;
        return node.Find("english")?.AsString() ?? node.Children.FirstOrDefault()?.AsString();
    }
}
