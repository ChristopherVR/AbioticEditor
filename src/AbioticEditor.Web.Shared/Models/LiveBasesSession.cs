using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live BASES editing session, implementing the same <see cref="IWorldBasesSession"/> the file
/// session does so <c>WorldBasesTab</c> renders unchanged for either host - see
/// <see cref="LiveContainersSession"/>/<see cref="LiveNpcSession"/> for the immediate-apply,
/// re-read-after-write pattern. Upgrade availability comes from the agent directory.
/// The bundled agent disables bench upgrades after native crashes in the attempted calls.
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
    private bool _supportsBenchUpgrades;
    private bool _supportsBenchUpgradeRemoval;
    public string? Status { get; private set; }

    /// <summary>Always false: a rename or upgrade install already reached the running game by
    /// the time it returns, so there is never a client-side staged copy.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    private void Apply(LiveDeployableDirectory directory)
    {
        _byId = directory.Deployables.ToDictionary(d => d.Id, StringComparer.Ordinal);
        Deployables = directory.Deployables
            .Select(d => new WorldDeployable(d.Id, d.ClassName, d.X, d.Y, d.Z, d.HasInventory, d.StoredItemCount, d.CustomName,
                d.InstalledUpgrades.Count > 0 ? d.InstalledUpgrades : null, d.PaintColor))
            .ToList();
        IsHost = directory.IsHost;
        _supportsBenchUpgrades = directory.SupportsBenchUpgrades;
        _supportsBenchUpgradeRemoval = directory.SupportsBenchUpgradeRemoval;
        Changed?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    public async Task SetCustomNameAsync(string deployableId, string? customName, CancellationToken cancellationToken = default)
    {
        await _channel.SetCustomNameAsync(deployableId, customName, cancellationToken).ConfigureAwait(false);
        Status = null;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IWorldBasesSession.SetPaintColorAsync(string deployableId, int? colorValue, CancellationToken cancellationToken)
    {
        if (!IsHost) throw new NotSupportedException("Only the host can change deployables.");
        await _channel.SetPaintColorAsync(deployableId, colorValue ?? DeployablePaintCatalog.NoneValue, cancellationToken)
            .ConfigureAwait(false);
        Status = null;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    bool IWorldBasesSession.AppliesImmediately => true;
    bool IWorldBasesSession.SupportsContainerPeek => false;

    bool IWorldBasesSession.BenchSupportsUpgrades(string deployableId)
        => _supportsBenchUpgrades && IsHost && _byId.TryGetValue(deployableId, out var deployable) && deployable.SupportsUpgrades && deployable.CanEditUpgrades;

    IReadOnlyList<string> IWorldBasesSession.BenchInstalledUpgrades(string deployableId)
        => _byId.TryGetValue(deployableId, out var deployable) ? deployable.InstalledUpgrades : [];

    async Task<bool> IWorldBasesSession.SetBenchUpgradeAsync(string deployableId, string row, bool installed, CancellationToken cancellationToken)
    {
        if (!IsHost || !_supportsBenchUpgrades || !_byId.TryGetValue(deployableId, out var current) || !current.CanEditUpgrades)
            throw new NotSupportedException("This bench cannot be edited by the connected agent.");
        if (!installed && !_supportsBenchUpgradeRemoval)
        {
            throw new NotSupportedException(
                "Removing an installed bench upgrade live isn't supported - no game function does it. Edit the save file instead.");
        }

        await _channel.SetBenchUpgradeAsync(deployableId, row, installed, cancellationToken).ConfigureAwait(false);
        Status = null;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        var tagRow = row.StartsWith("ItemTransporter", StringComparison.Ordinal) ? "ItemTransporter" : row;
        return _byId.TryGetValue(deployableId, out var deployable) && deployable.InstalledUpgrades.Contains(tagRow) == installed;
    }
}
