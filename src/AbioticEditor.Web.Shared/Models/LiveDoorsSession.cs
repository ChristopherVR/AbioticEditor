using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>Live door session: one door edit per action, applied immediately, then re-read.</summary>
public sealed class LiveDoorsSession
{
    private readonly LiveDoorsChannel _channel;

    private LiveDoorsSession(LiveDoorsChannel channel, LiveDoorDirectory directory)
    {
        _channel = channel;
        Doors = directory.Doors;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveDoorsSession> ConnectAsync(
        LiveDoorsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveDoorsSession(channel, directory);
    }

    public IReadOnlyList<LiveDoor> Doors { get; private set; }
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Doors = directory.Doors;
        IsHost = directory.IsHost;
    }

    public async Task ApplyAsync(LiveDoorEdit edit, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync([edit], cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
