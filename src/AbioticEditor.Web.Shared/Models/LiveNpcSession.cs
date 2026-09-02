using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live NPC editing session: unlike the vitals/skills sessions, edits here apply immediately, one
/// NPC at a time, rather than staging until an APPLY click. NPC rosters change constantly on
/// their own (wildlife wanders, things die), so a big staged batch could easily be edited against
/// an already-stale list by the time it was applied; one action, one round trip, then refresh
/// keeps what is on screen honest.
/// </summary>
public sealed class LiveNpcSession
{
    private readonly LiveNpcChannel _channel;

    private LiveNpcSession(LiveNpcChannel channel, LiveNpcDirectory directory)
    {
        _channel = channel;
        Npcs = directory.Npcs;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveNpcSession> ConnectAsync(
        LiveNpcChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveNpcSession(channel, directory);
    }

    public IReadOnlyList<LiveNpc> Npcs { get; private set; }
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Npcs = directory.Npcs;
        IsHost = directory.IsHost;
    }

    public async Task ApplyAsync(LiveNpcEdit edit, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync([edit], cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
