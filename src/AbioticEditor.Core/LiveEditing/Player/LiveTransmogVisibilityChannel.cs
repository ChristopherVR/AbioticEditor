namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live per-slot "hide this armor piece" toggle - the running-game counterpart to the file
/// editor's <c>TransmogVisibility</c> row on <c>PlayerTransmogTab</c>. Grounded in a real
/// client-authoritative RPC pair declared directly on <c>Abiotic_TransmogInventoryComp_C</c> - the
/// SAME component <see cref="LiveInventoryChannel"/> already reads/writes for the "transmog" kind
/// (see <c>transmog.get</c>/<c>transmog.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/transmog.lua</c> for the pak evidence):
/// <c>Request_ChangeTransmogVisibilityFlag(Index, Item)</c>. Only the first six flags are exposed
/// - the tab only shows the six visual gear roles (see
/// <c>docs/reference/research/research-transmog-appearance.md</c>).
/// </summary>
public sealed class LiveTransmogVisibilityChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads the six visible-slot flags for <paramref name="playerId"/>, or the local
    /// player when omitted.</summary>
    public async Task<IReadOnlyList<LiveTransmogVisibilityFlag>> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<DirectoryWire>("transmog.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return (wire.Visibility ?? []).Select(f => new LiveTransmogVisibilityFlag(f.Index, f.IsVisible)).ToList();
    }

    /// <summary>Sets one slot's visibility immediately.</summary>
    public Task SetAsync(int index, bool isVisible, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("transmog.set",
            new SetWire([new FlagWire(index, isVisible)], playerId), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record FlagWire(int Index, bool IsVisible);
    private sealed record DirectoryWire(IReadOnlyList<FlagWire>? Visibility);
    private sealed record SetWire(IReadOnlyList<FlagWire> Visibility, string? PlayerId);
}

/// <summary>One transmog visibility flag, as listed by
/// <see cref="LiveTransmogVisibilityChannel.GetAsync"/>.</summary>
public sealed record LiveTransmogVisibilityFlag(int Index, bool IsVisible);
