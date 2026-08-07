using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Steam;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Creates the narrowly scoped Steam Community links that a local host may hand to
/// the operating system. Authentication stays in the user's browser or Steam client;
/// this process never receives, persists, or forwards Steam cookies or credentials.
/// </summary>
public static class SteamCommunityLinks
{
    public static Uri SignIn { get; } = new("https://steamcommunity.com/login/home/");

    /// <summary>The signed-in user's own Steam privacy settings page (native PRIVACY SETTINGS).</summary>
    public static Uri PrivacySettings { get; } = new("https://steamcommunity.com/my/edit/settings");

    public static bool TryCreateProfile(string? steamId, out Uri? profile)
        => TryCreate(steamId, suffix: null, out profile);

    public static bool TryCreateAchievements(string? steamId, out Uri? achievements)
        => TryCreate(steamId, $"stats/{SteamAchievements.AppId}/achievements", out achievements);

    /// <summary>
    /// The native VIEW IN BROWSER flow: Steam community sign-in, then a redirect to this
    /// profile's Abiotic Factor achievements page. The sign-in happens entirely in the
    /// user's own browser; this process never sees the resulting session.
    /// </summary>
    public static bool TryCreateSignInAndViewAchievements(string? steamId, out Uri? signInAndView)
    {
        signInAndView = null;
        if (!PlayerIdentifier.IsSteamId(steamId)) return false;
        signInAndView = new Uri($"https://steamcommunity.com/login/home/?goto=profiles/{steamId}/stats/{SteamAchievements.AppId}/achievements", UriKind.Absolute);
        return true;
    }

    private static bool TryCreate(string? steamId, string? suffix, out Uri? uri)
    {
        uri = null;
        if (!PlayerIdentifier.IsSteamId(steamId)) return false;

        var path = suffix is null ? $"profiles/{steamId}" : $"profiles/{steamId}/{suffix}";
        uri = new Uri($"https://steamcommunity.com/{path}", UriKind.Absolute);
        return true;
    }
}
