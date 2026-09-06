using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>Live dropped-item session: list what is lying around, remove chosen items immediately.</summary>
public sealed class LiveDroppedItemsSession
{
    private readonly LiveDroppedItemsChannel _channel;

    private LiveDroppedItemsSession(LiveDroppedItemsChannel channel, LiveDroppedItemDirectory directory)
    {
        _channel = channel;
        Items = directory.Items;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveDroppedItemsSession> ConnectAsync(
        LiveDroppedItemsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveDroppedItemsSession(channel, directory);
    }

    public IReadOnlyList<LiveDroppedItem> Items { get; private set; }
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Items = directory.Items;
        IsHost = directory.IsHost;
    }

    public async Task RemoveAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var removed = await _channel.RemoveAsync(ids, cancellationToken).ConfigureAwait(false);
        Status = removed == 1
            ? "Removed 1 item from the running game."
            : $"Removed {removed} items from the running game.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
