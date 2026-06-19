using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="PlayerIdentifier"/>: the opaque-string identity helpers that let non-Steam
/// (Game Pass / Epic) saves be first-class. A SteamID64 is just one valid id.
/// </summary>
public class PlayerIdentifierTests
{
    [Theory]
    [InlineData("76561197993781479", true)]   // real 17-digit SteamID64
    [InlineData("76561198000000000", true)]
    [InlineData("msft-1A2B3C", false)]          // non-Steam token
    [InlineData("7656119799378147", false)]     // 16 digits - too short
    [InlineData("765611979937814790", false)]   // 18 digits - too long
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSteamId_matchesOnlyThe17DigitForm(string? id, bool expected)
        => Assert.Equal(expected, PlayerIdentifier.IsSteamId(id));

    [Theory]
    [InlineData("76561197993781479", true)]
    [InlineData("msft-1A2B3C", true)]
    [InlineData("epic_player.01", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a/b", false)]      // path separator
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]      // drive colon
    [InlineData("a*b", false)]      // wildcard
    [InlineData("CON", false)]      // Windows reserved device name
    [InlineData("..", false)]
    public void IsSafeFileToken_rejectsUnsafeNames(string? id, bool expected)
        => Assert.Equal(expected, PlayerIdentifier.IsSafeFileToken(id));

    [Fact]
    public void IsSafeFileToken_rejectsOverlongTokens()
        => Assert.False(PlayerIdentifier.IsSafeFileToken(new string('a', PlayerIdentifier.MaxLength + 1)));

    [Theory]
    [InlineData("PlayerData/Player_76561197993781479.sav", "76561197993781479")]
    [InlineData("/x/Player_msft-1A2B3C.sav", "msft-1A2B3C")]
    [InlineData("WorldSave_Facility.sav", null)]
    [InlineData("Player_.sav", null)] // prefix only, no id
    public void TryParseFromPlayerFileName_extractsTheOpaqueId(string path, string? expected)
    {
        var ok = PlayerIdentifier.TryParseFromPlayerFileName(path, out var id);
        if (expected is null)
        {
            Assert.False(ok);
        }
        else
        {
            Assert.True(ok);
            Assert.Equal(expected, id);
        }
    }

    [Fact]
    public void TryParseSteamId_onlySucceedsForSteamIds()
    {
        Assert.True(PlayerIdentifier.TryParseSteamId("76561197993781479", out var v));
        Assert.Equal(76561197993781479UL, v);
        Assert.False(PlayerIdentifier.TryParseSteamId("msft-1A2B3C", out _));
    }
}
