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
/// <summary>
/// <paramref name="X"/>/<paramref name="Y"/>/<paramref name="Z"/> are the actor's real world
/// position (round 89, added so chemistry benches can list closest-to-the-player first, the same
/// "nearby" convenience containers/dropped items already have) - always present now, since
/// care.lua's own actorLocation() helper never fails (falls back to 0,0,0 rather than omitting
/// the field), unlike WorldContainer's X/Y/Z which stay 0 for every non-live source instead.
/// </summary>
public sealed record LiveCareEntry(string Id, string Label, IReadOnlyList<LiveCareField> Fields, string? ContainerId = null,
    double X = 0, double Y = 0, double Z = 0)
{
    public double DistanceTo(double x, double y, double z) => Math.Sqrt((X - x) * (X - x) + (Y - y) * (Y - y) + (Z - z) * (Z - z));
}
public sealed record LiveCareField(string Id, string Label, string? Value, string Kind, bool Editable,
    IReadOnlyList<string>? Options = null, int? Maximum = null);
