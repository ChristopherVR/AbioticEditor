using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s vitals slice: implements the
/// same <see cref="IPlayerVitalsSession"/> boundary the <c>PlayerVitalsTab</c> widget already
/// binds to (see <c>PlayerVitals.cs</c>), so that widget needs zero changes to work against a
/// running game instead of a loaded file. Unlike the file session, there is no local "staged
/// until Save" backup: <see cref="SaveAsync"/> pushes straight to the live game, and that push
/// cannot be undone the way a file write's <c>.bak</c> can - <see cref="Status"/> says so.
/// </summary>
public sealed class LivePlayerVitalsSession : IPlayerVitalsSession
{
    private readonly LivePlayerVitalsChannel _channel;
    private PlayerVitals _original;
    private string? _playerId;

    private LivePlayerVitalsSession(LivePlayerVitalsChannel channel, string? playerId, PlayerVitals initial)
    {
        _channel = channel;
        _playerId = playerId;
        Vitals = initial;
        _original = initial.Clone();
    }

    /// <summary>
    /// Connects and reads the current vitals for <paramref name="playerId"/> (or the local
    /// player when omitted) to seed the session. Reads twice, a short beat apart, and keeps only
    /// the second read: connecting right as a world finishes loading in can catch the health
    /// component mid-replication, and unlike the "missing field" shape main.lua's vitals.get
    /// already guards against (see its own comment on the body-health-showing-0 report), a
    /// genuinely valid-looking but stale number - reported live as HEAD reading a flat 50% moments
    /// after loading in, when the character was actually at full health - passes that guard
    /// without tripping it. Best-effort: there is no way to prove replication has actually
    /// finished, only that it is more likely to have after a short wait.
    /// </summary>
    public static async Task<LivePlayerVitalsSession> ConnectAsync(
        LivePlayerVitalsChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
        var (stats, health) = await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return new LivePlayerVitalsSession(channel, playerId, ToVitals(stats, health));
    }

    public PlayerVitals Vitals { get; private set; }

    public bool IsDirty => !SameVitals(Vitals, _original);

    public string? Status { get; private set; }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _channel.SetAsync(
            new CharacterStats(Vitals.Hunger, Vitals.Thirst, Vitals.Sanity, Vitals.Fatigue,
                Vitals.Continence, (int)Math.Round(Vitals.Money)),
            new LimbHealth(Vitals.Head, Vitals.Torso, Vitals.LeftArm, Vitals.RightArm, Vitals.LeftLeg, Vitals.RightLeg),
            _playerId, cancellationToken).ConfigureAwait(false);
        _original = Vitals.Clone();
        Status = null;
    }

    public void Revert()
    {
        Vitals = _original.Clone();
        Status = "Changes reverted.";
    }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so damage/healing taken in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>Re-reads the live player's vitals, discarding any unsaved local edits.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var (stats, health) = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        Vitals = ToVitals(stats, health);
        _original = Vitals.Clone();
        Status = null;
    }

    /// <summary>Switches which connected player this session edits (discarding any unsaved local
    /// edits for the previous one) and re-reads that player's vitals.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PlayerVitals ToVitals(CharacterStats stats, LimbHealth health) => new()
    {
        Hunger = stats.Hunger, Thirst = stats.Thirst, Sanity = stats.Sanity,
        Fatigue = stats.Fatigue, Continence = stats.Continence, Money = stats.Money,
        Head = health.Head, Torso = health.Torso, LeftArm = health.LeftArm,
        RightArm = health.RightArm, LeftLeg = health.LeftLeg, RightLeg = health.RightLeg,
    };

    private static bool SameVitals(PlayerVitals left, PlayerVitals right) =>
        left.Hunger == right.Hunger && left.Thirst == right.Thirst && left.Sanity == right.Sanity
        && left.Fatigue == right.Fatigue && left.Continence == right.Continence && left.Money == right.Money
        && left.Head == right.Head && left.Torso == right.Torso && left.LeftArm == right.LeftArm
        && left.RightArm == right.RightArm && left.LeftLeg == right.LeftLeg && left.RightLeg == right.RightLeg;
}
