using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live world-container session: like <see cref="LiveInventorySession"/>, one slot applies per
/// action (item id + count + durability together), immediately, then the container list is
/// re-read so what is on screen stays honest.
/// </summary>
public sealed class LiveContainersSession
{
    private readonly LiveContainersChannel _channel;

    private LiveContainersSession(LiveContainersChannel channel, LiveContainerDirectory directory)
    {
        _channel = channel;
        Containers = directory.Containers;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveContainersSession> ConnectAsync(
        LiveContainersChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveContainersSession(channel, directory);
    }

    public IReadOnlyList<LiveContainer> Containers { get; private set; }
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Containers = directory.Containers;
        IsHost = directory.IsHost;
    }

    public async Task ApplyAsync(string containerId, LiveContainerSlotEdit edit, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync(containerId, [edit], cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
