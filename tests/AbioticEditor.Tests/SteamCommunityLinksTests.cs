using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class SteamCommunityLinksTests
{
    [Fact]
    public void Sign_in_link_uses_the_official_https_community_origin()
    {
        Assert.Equal("https", SteamCommunityLinks.SignIn.Scheme);
        Assert.Equal("steamcommunity.com", SteamCommunityLinks.SignIn.Host);
    }

    [Fact]
    public void Valid_steam_id_creates_profile_and_achievement_links()
    {
        const string steamId = "76561197993781479";

        Assert.True(SteamCommunityLinks.TryCreateProfile(steamId, out var profile));
        Assert.Equal($"https://steamcommunity.com/profiles/{steamId}", profile!.AbsoluteUri.TrimEnd('/'));
        Assert.True(SteamCommunityLinks.TryCreateAchievements(steamId, out var achievements));
        Assert.Equal($"https://steamcommunity.com/profiles/{steamId}/stats/427410/achievements", achievements!.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../../etc/passwd")]
    [InlineData("not-a-steam-id")]
    public void Invalid_identifier_does_not_create_external_links(string? identifier)
    {
        Assert.False(SteamCommunityLinks.TryCreateProfile(identifier, out var profile));
        Assert.Null(profile);
        Assert.False(SteamCommunityLinks.TryCreateAchievements(identifier, out var achievements));
        Assert.Null(achievements);
    }
}
