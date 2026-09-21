using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Adapts the staged world feature session to the same chemistry surface used by live worlds.
/// All writes remain in the wrapped save session until its normal Save action persists them.
/// </summary>
public sealed class OfflineChemistryBenchSession : IChemistryBenchSession
{
    private readonly IWorldFeaturesSession _session;
    public OfflineChemistryBenchSession(IWorldFeaturesSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    // Read through the wrapped session on every render. Revert clears WorldSaveSession's staged
    // feature tree, so retaining a snapshot here would leave the shared tab showing flask values
    // that are no longer pending in the save.
    public IReadOnlyList<LiveCareEntry> Entries
        => _session.MapFeature("chemistry-benches")?.Entries.Select(ToLiveEntry).ToArray() ?? [];
    public bool IsHost => true;
    public bool AppliesImmediately => false;
    public event Action? Changed;

    public async Task<WorldEditResult> SetFieldAsync(string entryKey, string fieldId, string? value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _session.SetMapFeatureField("chemistry-benches", entryKey, fieldId, value);
        Changed?.Invoke();
        return result;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private static LiveCareEntry ToLiveEntry(WorldMapEntry entry)
        => new(entry.Key, entry.Label, entry.Fields.Select(ToLiveField).ToArray(), entry.LinkTargetId);

    private static LiveCareField ToLiveField(WorldMapField field)
        => new(field.Id, field.Label, field.Value, "text",
            field.Editable, field.Options);
}
