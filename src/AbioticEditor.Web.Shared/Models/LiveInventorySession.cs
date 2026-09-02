using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live inventory editing session: like <see cref="LiveNpcSession"/>, edits apply immediately one
/// slot at a time rather than staging until a page-level APPLY, so a multi-field edit (item id +
/// stack + durability together) is one deliberate action instead of three separate instant writes.
/// </summary>
public sealed class LiveInventorySession
{
    private readonly LiveInventoryChannel _channel;
    private string? _playerId;

    private LiveInventorySession(LiveInventoryChannel channel, string? playerId, IReadOnlyList<LiveInventorySlot> slots)
    {
        _channel = channel;
        _playerId = playerId;
        Slots = slots;
    }

    public static async Task<LiveInventorySession> ConnectAsync(
        LiveInventoryChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var slots = await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return new LiveInventorySession(channel, playerId, slots);
    }

    public IReadOnlyList<LiveInventorySlot> Slots { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Slots = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyAsync(LiveInventoryEdit edit, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync([edit], _playerId, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Switches which connected player this session edits and re-reads their inventory.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
