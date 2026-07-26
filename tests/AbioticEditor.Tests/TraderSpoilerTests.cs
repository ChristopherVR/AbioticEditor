using AbioticEditor.Core.Codex;

namespace AbioticEditor.Tests;

/// <summary>
/// Traders the player has not reached yet must not be named anywhere. The recipe book leaked
/// them: it printed who sells an item with no gate at all, so looking up a taco recipe named
/// Jimmy Sanders, a trader who only appears once the game is finished.
/// </summary>
public class TraderSpoilerTests
{
    private static Func<string, bool> Flags(params string[] set)
    {
        var known = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        return known.Contains;
    }

    [Fact]
    public void Post_game_traders_are_future_content_on_a_fresh_save()
    {
        var none = Flags();
        Assert.True(TraderSpoilers.IsFutureContent("Jimmy", none));
        Assert.True(TraderSpoilers.IsFutureContent("Thule", none));
    }

    /// <summary>The reported case: deep into the story but short of the ending still hides them.</summary>
    [Fact]
    public void Reaching_the_botanical_gardens_does_not_reveal_a_post_game_trader()
    {
        var partway = Flags("Office_NewGameStarted", "Security_Entered", "Res_EnteredBotanicals");
        Assert.True(TraderSpoilers.IsFutureContent("Jimmy", partway));
    }

    [Fact]
    public void Finishing_the_game_reveals_them()
    {
        var finished = Flags("EndBossDefeated");
        Assert.False(TraderSpoilers.IsFutureContent("Jimmy", finished));
        Assert.False(TraderSpoilers.IsFutureContent("Thule", finished));
    }

    [Fact]
    public void Early_traders_are_not_hidden_once_their_own_moment_has_passed()
    {
        var started = Flags("Office_NewGameStarted");
        Assert.False(TraderSpoilers.IsFutureContent("Warren", started));

        var security = Flags("Office_NewGameStarted", "Security_Entered");
        Assert.False(TraderSpoilers.IsFutureContent("Chef", security));
        Assert.True(TraderSpoilers.IsFutureContent("Chef", Flags("Office_NewGameStarted")));
    }

    [Fact]
    public void Traders_with_no_curated_lore_are_never_hidden()
    {
        // A modded or unrecognised trader has no known gate, so concealing it would be a guess.
        Assert.False(TraderSpoilers.IsFutureContent("Fili", Flags()));
        Assert.False(TraderSpoilers.IsFutureContent("SomeModdedTrader", Flags()));
        Assert.False(TraderSpoilers.IsFutureContent(null, Flags()));
    }
}
