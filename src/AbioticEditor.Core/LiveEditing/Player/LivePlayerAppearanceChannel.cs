namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>Reads and updates the running character's replicated appearance.</summary>
public sealed class LivePlayerAppearanceChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    public Task<LiveAppearanceDirectory> GetAsync(string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<LiveAppearanceDirectory>("appearance.get", new PlayerWire(playerId), cancellationToken);
    public Task SetAsync(string propertyName, string rowName, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowName);
        return _channel.RequestAsync<object?>("appearance.set", new EditWire(playerId, propertyName, rowName), cancellationToken);
    }
    public Task SaveProfileAsync(string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("appearance.save", new PlayerWire(playerId), cancellationToken);

    private sealed record PlayerWire(string? PlayerId);
    private sealed record EditWire(string? PlayerId, string PropertyName, string RowName);
}

public sealed record LiveAppearanceDirectory(IReadOnlyDictionary<string, string>? Fields, bool CanEdit = false,
    bool CanSaveProfile = false, bool HasProfileChanges = false);
