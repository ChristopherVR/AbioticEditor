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
/// on a player save, leaving money alone. "Full" is 100 for four of them and 0 for fatigue:
/// the game's fatigue climbs from 0 (just slept) upwards as you stay awake, so a rested
/// character has 0 fatigue, not 100 (an earlier build of this sample wrote 100, which is a
/// character about to pass out). Only the needs that are actually off full are changed, so a
/// healthy save reports no change (and the host skips the backup + write entirely).
/// </summary>
public sealed class RepairNeedsOperation : ISaveOperation
{
    private const double Full = 100d;
    private const double Rested = 0d;

    public string Id => "repair-needs";

    public string DisplayName => "Repair Needs";

    public string Description =>
        "Restores hunger, thirst, sanity and continence to 100 and fatigue to 0 (fully rested) "
        + "on a player save. Money is untouched.";

    public SaveKind AppliesTo => SaveKind.Player;

    public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
    {
        // Read the typed model over the host's already-loaded save: data.Raw IS context.Save,
        // so the writer mutates the very instance the host will persist.
        var data = PlayerSaveReader.ReadFrom(context.Save);
        var stats = data.Stats;

        // Count which needs are below full so the report is accurate and a healthy save no-ops.
        var below = new (string Name, bool OffFull)[]
        {
            ("hunger", stats.Hunger < Full),
            ("thirst", stats.Thirst < Full),
            ("sanity", stats.Sanity < Full),
            ("fatigue", stats.Fatigue > Rested),
            ("continence", stats.Continence < Full),
        }.Count(s => s.OffFull);

        if (below == 0)
        {
            return Task.FromResult(SaveOperationResult.NoChange("all needs are already full."));
        }

        var repaired = stats with
        {
            Hunger = Full,
            Thirst = Full,
            Sanity = Full,
            Fatigue = Rested,
            Continence = Full,
        };
        PlayerSaveWriter.ApplyStats(data, repaired);
        context.MarkChanged();
        context.Log.Info($"restored {below} need(s) to full.");

        return Task.FromResult(SaveOperationResult.Ok($"restored {below} need(s) to full.", below));
    }
}
