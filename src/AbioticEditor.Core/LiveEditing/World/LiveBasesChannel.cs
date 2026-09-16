namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world-bases editing: lists every deployable currently loaded (anything deriving from
/// <c>AbioticDeployed_ParentBP</c> - benches, furniture, defenses, containers) with its world
/// position, player-given name, and (round 77) bench-upgrade state, and lets a host rename one
/// and install or remove an upgrade module - see <c>bases.list</c>/<c>bases.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/bases.lua</c>. Both directions write the
/// bench's own GameplayTag container directly (never the native Has Upgrade/AddUpgrade calls,
/// which crashed the bridge); whether removal is available on the connected runtime is reported
/// per deployable as <see cref="LiveDeployable.CanEditUpgrades"/> and overall as
/// <see cref="LiveDeployableDirectory.SupportsBenchUpgradeRemoval"/>.
/// </summary>
public sealed class LiveBasesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveDeployableDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("bases.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var deployables = (wire.Deployables ?? [])
            .Select(d => new LiveDeployable(d.Id, d.ClassName, d.X, d.Y, d.Z, d.CustomName, d.HasInventory,
                d.StoredItemCount, d.SupportsUpgrades, d.InstalledUpgrades ?? [], d.CanEditUpgrades ?? wire.SupportsBenchUpgrades))
            .ToList();
        return new LiveDeployableDirectory(deployables, wire.IsHost, wire.SupportsBenchUpgrades, wire.SupportsBenchUpgradeRemoval);
    }

    /// <summary>Renames one deployable immediately. Host only.</summary>
    public Task SetCustomNameAsync(string deployableId, string? customName, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("bases.set", new SetWire(deployableId, customName, null, null), cancellationToken);

    /// <summary>Installs or removes one bench upgrade module immediately (<paramref name="installed"/>
    /// selects which). Host only, and only on deployables reporting <c>CanEditUpgrades</c>.</summary>
    public Task SetBenchUpgradeAsync(string deployableId, string row, bool installed, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("bases.set", new SetWire(deployableId, null, row, installed), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<DeployableWire>? Deployables, bool IsHost, bool SupportsBenchUpgrades, bool SupportsBenchUpgradeRemoval);
    private sealed record DeployableWire(string Id, string ClassName, double X, double Y, double Z,
        string? CustomName, bool HasInventory, int StoredItemCount, bool SupportsUpgrades,
        IReadOnlyList<string>? InstalledUpgrades, bool? CanEditUpgrades);
    private sealed record SetWire(string Id, string? CustomName, string? UpgradeRow, bool? UpgradeInstalled);
}

/// <summary>One loaded deployable. <paramref name="Id"/> is the game's own full object name for
/// this exact actor; <paramref name="ClassName"/> is its class name (e.g.
/// <c>Deployed_CraftingBench_Default_C</c>). <paramref name="SupportsUpgrades"/> and
/// <paramref name="InstalledUpgrades"/> are meaningful only for benches; every other deployable
/// reports <c>false</c>/empty.</summary>
public sealed record LiveDeployable(string Id, string ClassName, double X, double Y, double Z,
    string? CustomName, bool HasInventory, int StoredItemCount, bool SupportsUpgrades,
    IReadOnlyList<string> InstalledUpgrades, bool CanEditUpgrades = false);

/// <summary>Every loaded deployable, whether this process has host authority to change them, and
/// whether bench-upgrade installation is available live (yes, since round 77 - see
/// <see cref="LiveDeployable.SupportsUpgrades"/> per-row). Whether upgrade *removal* is also
/// available depends on <see cref="SupportsBenchUpgradeRemoval"/>: it writes the bench's
/// GameplayTag container directly instead of the crash-prone Has Upgrade/AddUpgrade calls, so an
/// older live agent that predates that change reports it unsupported.</summary>
public sealed record LiveDeployableDirectory(IReadOnlyList<LiveDeployable> Deployables, bool IsHost,
    bool SupportsBenchUpgrades, bool SupportsBenchUpgradeRemoval = false);
