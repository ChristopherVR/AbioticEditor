namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>Reads and edits loaded gardens, Power Chairs and chemistry flask summaries.</summary>
public sealed class LiveDeployedCareChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public Task<LiveCareDirectory> GetAsync(string featureId, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<LiveCareDirectory>("care.list", new { FeatureId = featureId }, cancellationToken);

    public Task SetAsync(string featureId, string id, string fieldId, object value, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("care.set", new { FeatureId = featureId, Id = id, FieldId = fieldId, Value = value }, cancellationToken);
}

public sealed record LiveCareDirectory(IReadOnlyList<LiveCareEntry> Entries, bool IsHost);
public sealed record LiveCareEntry(string Id, string Label, IReadOnlyList<LiveCareField> Fields, string? ContainerId = null);
public sealed record LiveCareField(string Id, string Label, string? Value, string Kind, bool Editable,
    IReadOnlyList<string>? Options = null, int? Maximum = null);
