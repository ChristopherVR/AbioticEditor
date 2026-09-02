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

    private LivePlayerVitalsSession(LivePlayerVitalsChannel channel, PlayerVitals initial)
    {
        _channel = channel;
        Vitals = initial;
        _original = initial.Clone();
    }

    /// <summary>Connects and reads the live player's current vitals to seed the session.</summary>
    public static async Task<LivePlayerVitalsSession> ConnectAsync(
        LivePlayerVitalsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var (stats, health) = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LivePlayerVitalsSession(channel, ToVitals(stats, health));
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
            cancellationToken).ConfigureAwait(false);
        _original = Vitals.Clone();
        Status = "Applied live - this took effect in the running game immediately.";
    }

    public void Revert()
    {
        Vitals = _original.Clone();
        Status = "Changes reverted.";
    }

    /// <summary>Re-reads the live player's vitals, discarding any unsaved local edits.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var (stats, health) = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Vitals = ToVitals(stats, health);
        _original = Vitals.Clone();
        Status = "Refreshed from the running game.";
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
