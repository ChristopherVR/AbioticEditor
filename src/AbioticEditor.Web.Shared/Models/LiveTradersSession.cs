using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live trader-availability session. The trader roster itself (<see cref="Roster"/>) is static
/// game data (<see cref="TraderCatalog"/>/<see cref="TraderVocabularyService"/>, no live read
/// needed - it never differs between a loaded save and a running game); what IS live is which
/// quest/story flags are currently set, which decides whether each trader/offer reads as unlocked.
/// See <see cref="LiveTradersChannel"/> for why this reuses the world-flag write path rather than
/// a trader-specific one.
/// </summary>
public sealed class LiveTradersSession
{
    private readonly LiveTradersChannel _channel;

    private LiveTradersSession(LiveTradersChannel channel, IReadOnlyList<TraderInfo> roster, LiveTraderFlags flags)
    {
        _channel = channel;
        Roster = roster;
        Flags = flags;
    }

    public static async Task<LiveTradersSession> ConnectAsync(
        LiveTradersChannel channel, IReadOnlyList<TraderInfo> roster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var flags = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveTradersSession(channel, roster, flags);
    }

    public IReadOnlyList<TraderInfo> Roster { get; }
    public LiveTraderFlags Flags { get; private set; }
    public string? Status { get; private set; }

    public bool IsHost => Flags.IsHost;

    public bool HasWorldFlag(string flag) => Flags.HasFlag(flag);

    public bool IsAvailable(TraderInfo trader) => trader.RequiredFlags.Count == 0 || trader.RequiredFlags.All(HasWorldFlag);

    /// <summary>Every flag gating this trader or any of its offers that is not yet set.</summary>
    public IReadOnlyList<string> MissingFlags(TraderInfo trader) => trader.RequiredFlags
        .Concat(trader.Sells.Where(offer => offer.RequiredFlag is not null).Select(offer => offer.RequiredFlag!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(flag => !HasWorldFlag(flag))
        .ToArray();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Flags = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets every flag in <paramref name="flags"/> live, through the game's own
    /// world-flag subsystem, then re-reads so the roster's gating reflects reality.</summary>
    public async Task UnlockAsync(IReadOnlyCollection<string> flags, CancellationToken cancellationToken = default)
    {
        if (flags.Count == 0) return;
        await _channel.UnlockAsync(flags, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
