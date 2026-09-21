using System.Globalization;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>Shares the offline care panels with loaded world objects, applying each edit immediately.</summary>
public sealed class LiveDeployedCareSession : IWorldFeaturesSession, IChemistryBenchSession
{
    private readonly LiveDeployedCareChannel _channel;
    private LiveCareDirectory _directory;
    private int _pendingOperations;

    private LiveDeployedCareSession(LiveDeployedCareChannel channel, string featureId, LiveCareDirectory directory)
        => (_channel, FeatureId, _directory) = (channel, featureId, directory);

    public static async Task<LiveDeployedCareSession> ConnectAsync(LiveDeployedCareChannel channel, string featureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (featureId is not ("garden-plots" or "power-chairs" or "chemistry-benches"))
            throw new ArgumentOutOfRangeException(nameof(featureId));
        return new(channel, featureId, await channel.GetAsync(featureId, cancellationToken).ConfigureAwait(false));
    }

    public string FeatureId { get; }
    public string Path => string.Empty;
    public IReadOnlyList<WorldDeployable> Deployables => [];
    public bool IsHost => _directory.IsHost;
    public bool AppliesImmediately => true;
    public bool IsDirty => Volatile.Read(ref _pendingOperations) > 0;
    public event Action? Changed;

    /// <summary>The raw directory entries, for a dedicated tab (e.g. chemistry benches) that
    /// needs more than the generic label/field-list shape <see cref="MapFeature"/> exposes.</summary>
    public IReadOnlyList<LiveCareEntry> Entries => _directory.Entries;

    /// <summary>
    /// A narrower write path than <see cref="SetMapFeatureField"/>: no integer/enum shape
    /// checking, for a field kind the generic map-feature editor does not know about (a chemistry
    /// bench flask, which takes a free-form item row id). Catches a rejected write from the game
    /// itself and reports it, instead of letting it surface as an unhandled exception the way
    /// <see cref="SetMapFeatureField"/> currently leaves to its own callers.
    /// </summary>
    public async Task<WorldEditResult> SetFieldAsync(string entryKey, string fieldId, string? value, CancellationToken cancellationToken = default)
    {
        if (!IsHost) return WorldEditResult.Failure("Only the host can change deployed objects.");
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _channel.SetAsync(FeatureId, entryKey, fieldId, value ?? string.Empty, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return WorldEditResult.Success;
        }
        catch (LiveAgentException exception)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return WorldEditResult.Failure(exception.Message);
        }
        finally { Interlocked.Decrement(ref _pendingOperations); }
    }

    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _directory = await _channel.GetAsync(FeatureId, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (featureId != FeatureId) return null;
        var title = FeatureId switch { "garden-plots" => "Garden plots", "power-chairs" => "Power chairs", _ => "Chemistry benches" };
        var entries = _directory.Entries.Select(entry => new WorldMapEntry(entry.Id, entry.Label,
            entry.Fields.Select(field => new WorldMapField(field.Id, field.Label, field.Value,
                field.Kind switch { "integer" => WorldFieldKind.Integer, "enum" => WorldFieldKind.Enum, _ => WorldFieldKind.Text },
                IsHost && field.Editable, field.Options,
                field.Maximum is { } maximum ? $"0 to {maximum.ToString(CultureInfo.InvariantCulture)}. Applies immediately." : null)).ToArray(),
            entry.ContainerId, entry.ContainerId is null ? null : "Edit flask contents")).ToArray();
        return new(FeatureId, title, "Loaded objects in the running world. Changes apply immediately.",
            "DeployedObjectMap", false, string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!IsHost) return WorldEditResult.Failure("Only the host can change deployed objects.");
        if (featureId != FeatureId) return WorldEditResult.Failure("Unknown deployed care feature.");
        var field = _directory.Entries.FirstOrDefault(entry => entry.Id == entryKey)?.Fields.FirstOrDefault(field => field.Id == fieldId);
        if (field is not { Editable: true }) return WorldEditResult.Failure("This field is unavailable or read-only.");
        object parsed;
        if (field.Kind == "integer")
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                || number < 0 || number > (field.Maximum ?? int.MaxValue))
                return WorldEditResult.Failure("Choose a whole number within the displayed range.");
            parsed = number;
        }
        else if (field.Kind == "enum" && value is not null && field.Options?.Contains(value, StringComparer.Ordinal) == true)
            parsed = value;
        else return WorldEditResult.Failure("Choose a valid value from the list.");
        if (field.Value == value) return WorldEditResult.NoChange;
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _channel.SetAsync(featureId, entryKey, fieldId, parsed).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
            return WorldEditResult.Success;
        }
        finally { Interlocked.Decrement(ref _pendingOperations); }
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("Deployed care does not remove objects."));
}
