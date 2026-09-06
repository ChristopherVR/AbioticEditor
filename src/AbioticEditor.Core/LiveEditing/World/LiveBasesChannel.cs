namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world-bases editing: lists every deployable currently loaded (anything deriving from
/// <c>AbioticDeployed_ParentBP</c> - benches, furniture, defenses, containers) with its world
/// position and player-given name, and lets a host rename one - see <c>bases.list</c>/
/// <c>bases.set</c> in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/bases.lua</c>.
/// Bench upgrades have no evidenced live write path (see that module's own comment) and are
/// never reported by this channel at all.
/// </summary>
public sealed class LiveBasesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveDeployableDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("bases.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var deployables = (wire.Deployables ?? [])
            .Select(d => new LiveDeployable(d.Id, d.ClassName, d.X, d.Y, d.Z, d.CustomName, d.HasInventory, d.StoredItemCount))
            .ToList();
        return new LiveDeployableDirectory(deployables, wire.IsHost);
    }

    /// <summary>Renames one deployable immediately. Host only.</summary>
    public Task SetCustomNameAsync(string deployableId, string? customName, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("bases.set", new SetWire(deployableId, customName), cancellationToken);

    // "supportsBenchUpgrades" also rides in the wire response (see areas/bases.lua) as a
    // documentation-only signal for anything inspecting the raw protocol; the C# side already
    // knows bench upgrades have no live path (LiveBasesSession.BenchSupportsUpgrades always
    // returns false), so it is intentionally not modelled as its own property here.
    private sealed record DirectoryWire(IReadOnlyList<DeployableWire>? Deployables, bool IsHost);
    private sealed record DeployableWire(string Id, string ClassName, double X, double Y, double Z,
        string? CustomName, bool HasInventory, int StoredItemCount);
    private sealed record SetWire(string Id, string? CustomName);
}

/// <summary>One loaded deployable. <paramref name="Id"/> is the game's own full object name for
/// this exact actor; <paramref name="ClassName"/> is its class name (e.g.
/// <c>Deployed_CraftingBench_Default_C</c>).</summary>
public sealed record LiveDeployable(string Id, string ClassName, double X, double Y, double Z,
    string? CustomName, bool HasInventory, int StoredItemCount);

/// <summary>Every loaded deployable and whether this process has host authority to rename them.
/// Bench upgrades have no live write path (see the module's own comment), so they never appear
/// here at all.</summary>
public sealed record LiveDeployableDirectory(IReadOnlyList<LiveDeployable> Deployables, bool IsHost);
