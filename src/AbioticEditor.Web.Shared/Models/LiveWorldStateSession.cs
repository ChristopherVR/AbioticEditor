using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live world clock and weather session. Each action (set the time, trigger weather, queue
/// tomorrow's weather) is one deliberate button press that applies immediately and then
/// re-reads the game, the same shape <see cref="LiveNpcSession"/> uses - the clock keeps
/// running on its own, so a staged batch would always be applied against a stale reading.
/// </summary>
public sealed class LiveWorldStateSession
{
    private readonly LiveWorldStateChannel _channel;

    private LiveWorldStateSession(LiveWorldStateChannel channel, LiveWorldState state)
    {
        _channel = channel;
        State = state;
    }

    public static async Task<LiveWorldStateSession> ConnectAsync(
        LiveWorldStateChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var state = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveWorldStateSession(channel, state);
    }

    public LiveWorldState State { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        State = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyAsync(LiveWorldStateEdit edit, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync(edit, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets the clock to <paramref name="hour"/>:<paramref name="minute"/> of the current day.</summary>
    public Task SetTimeAsync(int hour, int minute, CancellationToken cancellationToken = default)
        => ApplyAsync(new LiveWorldStateEdit(TimeSeconds: Math.Clamp(hour, 0, 23) * 3600 + Math.Clamp(minute, 0, 59) * 60), cancellationToken);
}
