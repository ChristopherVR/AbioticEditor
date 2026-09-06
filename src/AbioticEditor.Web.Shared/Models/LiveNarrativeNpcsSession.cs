using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live narrative-NPC editing session, implementing the same <see cref="IWorldNpcsSession"/> the
/// file session does so <c>WorldNpcsTab</c> renders unchanged for either host - round 77's
/// counterpart to <see cref="LiveVehiclesSession"/>/<see cref="LiveBasesSession"/>. Grounded in
/// <c>NarrativeNPC_ParentBP_C</c>'s own <c>IsCorpse</c>/<c>NarrativeState</c> properties and its
/// real <c>SetNewNarrativeState</c> setter - see <c>areas/narrative.lua</c>'s own comment.
/// <see cref="WorldNpc.State"/> here is the raw enum byte as a plain integer string, not the
/// file's own enum-name string; see the interface remarks for why that is fine.
/// </summary>
public sealed class LiveNarrativeNpcsSession : IWorldNpcsSession
{
    private readonly LiveNarrativeNpcsChannel _channel;

    private LiveNarrativeNpcsSession(LiveNarrativeNpcsChannel channel, LiveNarrativeNpcDirectory directory)
    {
        _channel = channel;
        Apply(directory);
    }

    public static async Task<LiveNarrativeNpcsSession> ConnectAsync(
        LiveNarrativeNpcsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveNarrativeNpcsSession(channel, directory);
    }

    public IReadOnlyList<WorldNpc> Npcs { get; private set; } = [];
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }
    public bool AppliesImmediately => true;

    private void Apply(LiveNarrativeNpcDirectory directory)
    {
        Npcs = directory.Npcs
            .Select(n => new WorldNpc(n.Id, n.IsCorpse, n.NarrativeState.ToString(System.Globalization.CultureInfo.InvariantCulture),
                n.X, n.Y, n.Z, IsPet: false, CustomName: null, NpcClass: n.Label))
            .ToList();
        IsHost = directory.IsHost;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    public async Task SetNpcAsync(string id, bool isDead, string? state, CancellationToken cancellationToken = default)
    {
        int? narrativeState = int.TryParse(state, out var value) ? value : null;
        await _channel.SetAsync([new LiveNarrativeNpcEdit(id, IsCorpse: isDead, NarrativeState: narrativeState)], cancellationToken)
            .ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
