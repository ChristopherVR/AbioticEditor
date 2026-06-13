using AbioticEditor.Core.Steam;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="SteamWebAchievements.ParseResponse"/> against the three real
/// response shapes the community endpoint produces. Permission errors come
/// back as a full HTML page served with a <c>text/xml</c> content type, and that
/// happens even for PUBLIC profiles whose separate "Game details" setting is not
/// public (verified live 2026-06-12).
/// </summary>
public class SteamWebAchievementTests
{
    // Trimmed copy of the real profile_fatalerror page Steam serves.
    private const string PermissionHtml = """
        <!DOCTYPE html>
        <html class=" responsive DesktopUI" lang="en">
        <head><title>Steam Community :: Error</title></head>
        <body>
        <div class="profile_fatalerror">
            <h1>Sorry!</h1>
            <h3>An error was encountered while processing your request:</h3>
            <div class="profile_fatalerror_message">You do not have permission to view these game stats.</div>
            <div class="profile_fatalerror_links">
                <a href="https://steamcommunity.com/profiles/76561197993781479" class="whiteLink">
                    Return to Tribbes's profile</a>
            </div>
        </div>
        </body>
        </html>
        """;

    [Fact]
    public void Permission_error_page_throws_typed_exception()
    {
        var ex = Assert.Throws<SteamGameDetailsPrivateException>(
            () => SteamWebAchievements.ParseResponse(PermissionHtml));
        Assert.Contains("You do not have permission to view these game stats", ex.Message);
    }

    [Fact]
    public void Extracts_message_from_error_page()
    {
        Assert.Equal(
            "You do not have permission to view these game stats.",
            SteamWebAchievements.ExtractHtmlError(PermissionHtml));
    }

    [Fact]
    public void Other_html_error_throws_with_steam_text()
    {
        var html = PermissionHtml.Replace(
            "You do not have permission to view these game stats.",
            "The specified profile could not be found.");
        var ex = Assert.Throws<InvalidOperationException>(
            () => SteamWebAchievements.ParseResponse(html));
        Assert.Contains("The specified profile could not be found", ex.Message);
    }

    [Fact]
    public void Xml_error_response_throws_with_steam_text()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <response><error><![CDATA[Invalid game specified.]]></error></response>
            """;
        var ex = Assert.Throws<InvalidOperationException>(
            () => SteamWebAchievements.ParseResponse(xml));
        Assert.Contains("Invalid game specified", ex.Message);
    }

    [Fact]
    public void Valid_xml_parses_achievements()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <playerstats>
                <game><gameName>Abiotic Factor</gameName></game>
                <achievements>
                    <achievement closed="1">
                        <iconClosed>https://cdn.example/unlocked.jpg</iconClosed>
                        <iconOpen>https://cdn.example/locked.jpg</iconOpen>
                        <name><![CDATA[First Day on the Job]]></name>
                        <apiname><![CDATA[ACH_TUTORIAL]]></apiname>
                        <description><![CDATA[Survive your first day.]]></description>
                        <unlockTimestamp>1715000000</unlockTimestamp>
                    </achievement>
                    <achievement closed="0">
                        <iconClosed>https://cdn.example/unlocked2.jpg</iconClosed>
                        <iconOpen>https://cdn.example/locked2.jpg</iconOpen>
                        <name><![CDATA[Hidden One]]></name>
                        <apiname><![CDATA[ACH_SECRET]]></apiname>
                        <description></description>
                    </achievement>
                </achievements>
            </playerstats>
            """;

        var result = SteamWebAchievements.ParseResponse(xml);

        Assert.Equal(2, result.Count);
        var first = result[0];
        Assert.Equal("ACH_TUTORIAL", first.ApiName);
        Assert.Equal("First Day on the Job", first.DisplayName);
        Assert.True(first.Unlocked);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1715000000), first.UnlockedAt);
        Assert.Equal("https://cdn.example/unlocked.jpg", first.IconUrl);
        Assert.False(result[1].Unlocked);
        Assert.Null(result[1].UnlockedAt);
    }
}
