using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;

namespace AbioticEditor.Samples.RepairNeeds;

/// <summary>Entry point: registers the one save operation this plugin provides.</summary>
public sealed class RepairNeedsPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        host.Log.Info("Repair Needs plugin configured.");
        registry.AddSaveOperation(new RepairNeedsOperation());
    }
}

/// <summary>
/// Tops every survival need (hunger, thirst, sanity, fatigue, continence) back up to full
/// on a player save, leaving money alone. A practical "fix-up": it also repairs the
/// delta-serialization case where a need the game never wrote reads back as 0 - this writes
/// the tag at 100. Only the needs that are actually below full are changed, so a healthy save
/// reports no change (and the host skips the backup + write entirely).
/// </summary>
public sealed class RepairNeedsOperation : ISaveOperation
{
    private const double Full = 100d;

    public string Id => "repair-needs";

    public string DisplayName => "Repair Needs";

    public string Description =>
        "Restores hunger, thirst, sanity, fatigue and continence to 100 on a player save "
        + "(also fixes needs that read as 0 because the game never wrote the tag). Money is untouched.";

    public SaveKind AppliesTo => SaveKind.Player;

    public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
    {
        // Read the typed model over the host's already-loaded save: data.Raw IS context.Save,
        // so the writer mutates the very instance the host will persist.
        var data = PlayerSaveReader.ReadFrom(context.Save);
        var stats = data.Stats;

        // Count which needs are below full so the report is accurate and a healthy save no-ops.
        var below = new (string Name, double Value)[]
        {
            ("hunger", stats.Hunger),
            ("thirst", stats.Thirst),
            ("sanity", stats.Sanity),
            ("fatigue", stats.Fatigue),
            ("continence", stats.Continence),
        }.Count(s => s.Value < Full);

        if (below == 0)
        {
            return Task.FromResult(SaveOperationResult.NoChange("all needs are already full."));
        }

        var repaired = stats with
        {
            Hunger = Full,
            Thirst = Full,
            Sanity = Full,
            Fatigue = Full,
            Continence = Full,
        };
        PlayerSaveWriter.ApplyStats(data, repaired);
        context.MarkChanged();
        context.Log.Info($"restored {below} need(s) to full.");

        return Task.FromResult(SaveOperationResult.Ok($"restored {below} need(s) to full.", below));
    }
}
