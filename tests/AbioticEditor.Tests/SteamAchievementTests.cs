using AbioticEditor.Core.Steam;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Local Steam achievement parsing. The stats-file discovery / schema-dump probes that
/// used to share this file live in AbioticEditor.Probes/SteamAchievementProbes.cs.
/// </summary>
public class SteamAchievementTests
{
    private readonly ITestOutputHelper _output;

    public SteamAchievementTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadFor_FixturePlayer_ParsesAchievements()
    {
        // The fixture saves are real SteamID64-named files; try the primary account.
        var result = SteamAchievements.LoadFor(76561197993781479);
        if (result is null)
        {
            _output.WriteLine("Steam stats unavailable on this machine — skipping.");
            return;
        }

        _output.WriteLine($"{result.Count} achievements, {result.Count(a => a.Unlocked)} unlocked");
        foreach (var a in result.Take(60))
        {
            _output.WriteLine($"  [{(a.Unlocked ? "X" : " ")}] {a.ApiName}: {a.DisplayName} — {a.Description}");
        }
        Assert.True(result.Count > 0, "schema parsed but no achievements found");
    }
}
