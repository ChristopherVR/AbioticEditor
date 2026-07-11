using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AbioticEditor.Core.Steam;

/// <summary>One achievement as reported by the Steam community endpoint.</summary>
public sealed record WebAchievement(
    string ApiName,
    string DisplayName,
    string? Description,
    bool Unlocked,
    string? IconUrl,
    string? IconGrayUrl,
    DateTimeOffset? UnlockedAt);

/// <summary>
/// Steam refused access to the profile's game stats. This happens even on PUBLIC
/// profiles: the "Game details" privacy dropdown is a separate setting from the
/// profile visibility, and it defaults to Friends Only.
/// </summary>
public sealed class SteamGameDetailsPrivateException : InvalidOperationException
{
    public SteamGameDetailsPrivateException(string message) : base(message) { }
}

/// <summary>
/// Reads a player's achievements from Steam's public community endpoint,
/// <c>steamcommunity.com/profiles/&lt;id64&gt;/stats/&lt;appid&gt;/achievements?xml=1</c>.
/// No API key needed, but the profile's <b>Game details</b> setting (not just the
/// profile itself) must be public. Complements the local appcache read: the web is
/// authoritative and works for accounts that never played on this machine.
/// </summary>
public static class SteamWebAchievements
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>
    /// Fetches achievements for <paramref name="steamId64"/>. Throws
    /// <see cref="SteamGameDetailsPrivateException"/> when Steam denies access to the
    /// stats, and <see cref="InvalidOperationException"/> with the readable cause for
    /// every other failure. When <paramref name="cookieHeader"/> is set (e.g. a captured
    /// <c>steamLoginSecure</c> session), the request runs as that signed-in user, which
    /// can see gated profiles the anonymous query cannot (own account, friends).
    /// </summary>
    public static async Task<IReadOnlyList<WebAchievement>> FetchAsync(
        long steamId64, int appId = SteamAchievements.AppId, string? cookieHeader = null)
    {
        var url = $"https://steamcommunity.com/profiles/{steamId64}/stats/{appId}/achievements?xml=1";
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Steam request failed: {ex.Message}");
        }

        return ParseResponse(body);
    }

    /// <summary>
    /// Parses the community endpoint's response body. Exposed for tests. Steam serves
    /// its HTML error page with a <c>text/xml</c> content type, so detection has to go
    /// by the body itself, not headers.
    /// </summary>
    public static IReadOnlyList<WebAchievement> ParseResponse(string body)
    {
        // Steam answers permission problems (and most other errors) with a full HTML
        // page instead of the XML document - even on this ?xml=1 endpoint.
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            var steamError = ExtractHtmlError(body);
            if (steamError is not null
                && steamError.Contains("permission", StringComparison.OrdinalIgnoreCase))
            {
                throw new SteamGameDetailsPrivateException(
                    $"Steam refused access to this profile's game stats (\"{steamError}\").");
            }
            throw new InvalidOperationException(
                steamError is null
                    ? "Steam returned an error page instead of achievement data."
                    : $"Steam returned an error page: {steamError}");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(body);
        }
        catch
        {
            throw new InvalidOperationException("Steam returned an unrecognized response (not XML).");
        }

        if (doc.Root?.Name.LocalName == "response" || doc.Descendants("error").Any())
        {
            var error = doc.Descendants("error").FirstOrDefault()?.Value ?? "no stats available";
            throw new InvalidOperationException($"Steam: {error}");
        }

        var result = new List<WebAchievement>();
        foreach (var a in doc.Descendants("achievement"))
        {
            var apiName = a.Element("apiname")?.Value;
            if (string.IsNullOrEmpty(apiName)) continue;

            var unlocked = a.Attribute("closed")?.Value == "1";
            DateTimeOffset? unlockedAt = null;
            if (long.TryParse(a.Element("unlockTimestamp")?.Value, out var ts) && ts > 0)
            {
                unlockedAt = DateTimeOffset.FromUnixTimeSeconds(ts);
            }

            result.Add(new WebAchievement(
                ApiName: apiName,
                DisplayName: a.Element("name")?.Value ?? apiName,
                Description: a.Element("description")?.Value,
                Unlocked: unlocked,
                IconUrl: a.Element("iconClosed")?.Value,
                IconGrayUrl: a.Element("iconOpen")?.Value,
                UnlockedAt: unlockedAt));
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                "Steam returned a stats page with no achievements in it.");
        }
        return result;
    }

    /// <summary>
    /// Pulls the human-readable message out of Steam's HTML error page
    /// ("An error was encountered while processing your request: &lt;message&gt;").
    /// </summary>
    public static string? ExtractHtmlError(string html)
    {
        // The message follows the "An error was encountered" heading as plain text in
        // its own element (e.g. "You do not have permission to view these game stats.").
        var m = Regex.Match(html, @"An error was encountered[^<]*</h3>\s*(?:<[^>]+>\s*)*([^<]+)<",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success)
        {
            var text = m.Groups[1].Value.Trim();
            if (text.Length > 0) return text;
        }

        // Fallback: the well-known permission phrase anywhere in the page.
        var p = Regex.Match(html, @"You do not have permission[^<]*", RegexOptions.IgnoreCase);
        return p.Success ? p.Value.Trim() : null;
    }
}
