namespace AbioticEditor.Tests;

using System.Reflection;
using AbioticEditor.Web.Models;

/// <summary>
/// Pins a non-obvious reflection contract in <c>LiveConnect.razor</c>'s periodic background
/// refresh loop (round 78, see <c>LiveSessionReflection.TryRefreshAsync</c>): it finds a
/// session's refresh method with <c>GetType().GetMethod("RefreshAsync", Type.EmptyTypes)</c>,
/// which only matches a genuine zero-parameter method. Every live session's real work method is
/// <c>RefreshAsync(CancellationToken cancellationToken = default)</c> - one parameter with a
/// default value - which that lookup does NOT match, C# default parameters being a call-site
/// convenience, not a second overload. Discovered live (round 82): a player picking up an item,
/// taking damage, or making any other in-game change never showed up in the editor on its own,
/// because this reflection call silently found nothing and skipped the refresh every single tick
/// - not an error, just a permanent no-op - for every session except the one
/// (<c>LiveDeployedCareSession</c>) that happened to already expose a true zero-arg overload.
/// Every session this loop can reach now also exposes one: <c>public Task RefreshAsync() =>
/// RefreshAsync(CancellationToken.None);</c>. This test fails loudly if a future session added to
/// <c>ActiveLiveSessions()</c> forgets it.
/// </summary>
public sealed class LiveSessionPeriodicRefreshContractTests
{
    // Every concrete type LiveConnect.razor's ActiveLiveSessions() can yield into the periodic
    // refresh loop, as of round 82. Keep this list in sync with that method.
    private static readonly Type[] PeriodicallyRefreshedSessionTypes =
    [
        typeof(LivePlayerVitalsSession), typeof(LivePlayerSkillsSession), typeof(LiveInventorySession),
        typeof(LivePlayerSpawnSession), typeof(LivePlayerCompanionsSession), typeof(LivePlayerRecipesSession),
        typeof(LivePlayerCodexSession), typeof(LivePlayerGeneralSession), typeof(LiveStorySession),
        typeof(LiveWorldFlagsSession), typeof(LiveDoorsSession), typeof(LiveDroppedItemsSession),
        typeof(LiveVehiclesSession), typeof(LivePetsSession), typeof(LiveNarrativeNpcsSession),
        typeof(LiveContainmentSession), typeof(LiveTradersSession), typeof(LivePortalsFeatureSession),
        typeof(LiveDeployedCareSession),
        // Round 91: back in the loop, with a zero-arg overload that re-reads only the watched
        // container (see LiveContainersSession.WatchedContainerId), never the world scan.
        typeof(LiveContainersSession),
        typeof(LiveButtonsFeatureSession),
    ];

    [Theory]
    [MemberData(nameof(SessionTypes))]
    public void Session_exposes_a_true_zero_argument_RefreshAsync_overload(Type sessionType)
    {
        var method = sessionType.GetMethod("RefreshAsync", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        Assert.True(method is not null,
            $"{sessionType.Name} has no public zero-argument RefreshAsync() overload, so LiveConnect's " +
            "periodic background refresh loop can never find it by reflection and will silently skip " +
            "this session forever. Add: public Task RefreshAsync() => RefreshAsync(CancellationToken.None);");
    }

    public static IEnumerable<object[]> SessionTypes() => PeriodicallyRefreshedSessionTypes.Select(t => new object[] { t });
}
