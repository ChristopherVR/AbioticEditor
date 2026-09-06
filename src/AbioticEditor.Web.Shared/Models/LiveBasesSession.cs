using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live BASES editing session, implementing the same <see cref="IWorldBasesSession"/> the file
/// session does so <c>WorldBasesTab</c> renders unchanged for either host - see
/// <see cref="LiveContainersSession"/>/<see cref="LiveNpcSession"/> for the immediate-apply,
/// re-read-after-write pattern this copies. Bench upgrades have no evidenced live write path
/// (see <c>areas/bases.lua</c>'s own comment): <see cref="BenchSupportsUpgrades"/> always
/// returns false and <see cref="SetBenchUpgradeAsync"/> always throws.
/// </summary>
public sealed class LiveBasesSession : IWorldBasesSession
{
    private readonly LiveBasesChannel _channel;

    private LiveBasesSession(LiveBasesChannel channel, LiveDeployableDirectory directory)
    {
        _channel = channel;
        Apply(directory);
    }

    public static async Task<LiveBasesSession> ConnectAsync(
        LiveBasesChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveBasesSession(channel, directory);
    }

    public IReadOnlyList<WorldDeployable> Deployables { get; private set; } = [];
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    private void Apply(LiveDeployableDirectory directory)
    {
        Deployables = directory.Deployables
            .Select(d => new WorldDeployable(d.Id, d.ClassName, d.X, d.Y, d.Z, d.HasInventory, d.StoredItemCount, d.CustomName))
            .ToList();
        IsHost = directory.IsHost;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    public async Task SetCustomNameAsync(string deployableId, string? customName, CancellationToken cancellationToken = default)
    {
        await _channel.SetCustomNameAsync(deployableId, customName, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    bool IWorldBasesSession.AppliesImmediately => true;
    bool IWorldBasesSession.SupportsContainerPeek => false;
    bool IWorldBasesSession.BenchSupportsUpgrades(string deployableId) => false;
    IReadOnlyList<string> IWorldBasesSession.BenchInstalledUpgrades(string deployableId) => [];
    Task<bool> IWorldBasesSession.SetBenchUpgradeAsync(string deployableId, string row, bool installed, CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "Installing or removing bench upgrades live isn't supported yet - see areas/bases.lua.");
}
