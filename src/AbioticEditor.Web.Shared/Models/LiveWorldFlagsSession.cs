using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live quest/story flag session: one flag toggled per action, applied immediately and then
/// re-read (the world's own triggers can set flags at any moment, so the list is refreshed
/// after every write rather than trusted from before it).
/// </summary>
public sealed class LiveWorldFlagsSession
{
    private readonly LiveWorldFlagsChannel _channel;

    private LiveWorldFlagsSession(LiveWorldFlagsChannel channel, LiveWorldFlagDirectory directory)
    {
        _channel = channel;
        Flags = directory.Flags;
        IsHost = directory.IsHost;
    }

    public static async Task<LiveWorldFlagsSession> ConnectAsync(
        LiveWorldFlagsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveWorldFlagsSession(channel, directory);
    }

    public IReadOnlyList<LiveWorldFlag> Flags { get; private set; }
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    /// <summary>The raw names of every flag currently set, for the file editor's flag-gating helpers.</summary>
    public IReadOnlySet<string> SetFlags => Flags.Where(f => f.IsSet).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Flags = directory.Flags;
        IsHost = directory.IsHost;
    }

    /// <summary>Sets or clears the given flags in one request, then re-reads the world.</summary>
    public async Task ApplyAsync(IReadOnlyList<LiveWorldFlag> edits, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync(edits, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
