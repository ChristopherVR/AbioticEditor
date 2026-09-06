using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live BASES editing session, implementing the same <see cref="IWorldBasesSession"/> the file
/// session does so <c>WorldBasesTab</c> renders unchanged for either host - see
/// <see cref="LiveContainersSession"/>/<see cref="LiveNpcSession"/> for the immediate-apply,
/// re-read-after-write pattern this copies. Bench upgrade installation is grounded in the
/// bench's own <c>AddUpgrade</c> function (round 77); removal has no evidenced live function and
/// always throws - see <c>areas/bases.lua</c>'s own comment for what remains unverified (the
/// upgrade row-handle's <c>DataTablePath</c> is reconstructed from the pak's asset location, not
/// fetched from a live enumeration function, since none exists for this table).
/// </summary>
public sealed class LiveBasesSession : IWorldBasesSession
{
    private readonly LiveBasesChannel _channel;
    private Dictionary<string, LiveDeployable> _byId = new(StringComparer.Ordinal);

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
        _byId = directory.Deployables.ToDictionary(d => d.Id, StringComparer.Ordinal);
        Deployables = directory.Deployables
            .Select(d => new WorldDeployable(d.Id, d.ClassName, d.X, d.Y, d.Z, d.HasInventory, d.StoredItemCount, d.CustomName,
                d.InstalledUpgrades.Count > 0 ? d.InstalledUpgrades : null))
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

    bool IWorldBasesSession.BenchSupportsUpgrades(string deployableId)
        => _byId.TryGetValue(deployableId, out var deployable) && deployable.SupportsUpgrades;

    IReadOnlyList<string> IWorldBasesSession.BenchInstalledUpgrades(string deployableId)
        => _byId.TryGetValue(deployableId, out var deployable) ? deployable.InstalledUpgrades : [];

    async Task<bool> IWorldBasesSession.SetBenchUpgradeAsync(string deployableId, string row, bool installed, CancellationToken cancellationToken)
    {
        if (!installed)
        {
            throw new NotSupportedException(
                "Removing an installed bench upgrade live isn't supported - no game function does it. Edit the save file instead.");
        }

        await _channel.SetBenchUpgradeAsync(deployableId, row, installed: true, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return _byId.TryGetValue(deployableId, out var deployable) && deployable.InstalledUpgrades.Contains(row);
    }
}
