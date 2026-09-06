using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s General-tab slice: implements
/// the same <see cref="IPlayerGeneralSession"/> boundary <c>PlayerGeneralTab</c> already binds to
/// (see <c>IPlayerGeneralSession.cs</c>), so that widget needs zero changes to work against a
/// running game instead of a loaded file. ITEMS SEEN and MAPS discover immediately, one network
/// round trip per batch; ITEMS CRAFTED is read-only (<see cref="IPlayerDiscoverySection.CanDiscoverAll"/>
/// is false - see <c>LivePlayerGeneralChannel</c>'s remarks) and the account/owner-id change is
/// unavailable entirely (<see cref="CanChangeOwnerId"/> is always false - there is no live concept
/// of "which save file this character came from" to change). BACKGROUND applies live immediately
/// (a real, grounded property write - see <see cref="SetBackgroundAsync"/>); TRAITS is a
/// read-only readout (see <see cref="IPlayerGeneralSession.Traits"/>'s remarks).
/// </summary>
public sealed class LivePlayerGeneralSession : IPlayerGeneralSession
{
    private readonly LivePlayerGeneralChannel _channel;
    private string? _playerId;
    private readonly HashSet<string> _itemsSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _itemsCrafted = new(StringComparer.Ordinal);
    private readonly HashSet<string> _maps = new(StringComparer.Ordinal);
    private List<string> _traits = [];

    private LivePlayerGeneralSession(LivePlayerGeneralChannel channel, string? playerId, string? ownerId)
    {
        _channel = channel;
        _playerId = playerId;
        OwnerId = ownerId;

        ItemsSeen = new DelegateDiscoverySection(() => _itemsSeen, canDiscoverAll: true,
            async vocabulary =>
            {
                var ids = CleanNew(vocabulary, _itemsSeen);
                if (ids.Count == 0) return;
                await _channel.SetAsync(itemsSeen: ids, playerId: _playerId).ConfigureAwait(false);
                foreach (var id in ids) _itemsSeen.Add(id);
                Status = "Applied live - this took effect in the running game immediately.";
            });
        ItemsCrafted = new DelegateDiscoverySection(() => _itemsCrafted, canDiscoverAll: false,
            _ => throw new InvalidOperationException(
                "Crafted items can't be discovered live - the running game tracks them automatically but exposes no function to mark one crafted on demand."));
        Maps = new DelegateDiscoverySection(() => _maps, canDiscoverAll: true,
            async vocabulary =>
            {
                var ids = CleanNew(vocabulary, _maps);
                if (ids.Count == 0) return;
                await _channel.SetAsync(maps: ids, playerId: _playerId).ConfigureAwait(false);
                foreach (var id in ids) _maps.Add(id);
                Status = "Applied live - this took effect in the running game immediately.";
            });
    }

    /// <summary>Connects and reads which items/maps the running character already knows, for
    /// <paramref name="playerId"/> (or the local player when omitted). <paramref name="ownerId"/>
    /// is purely informational live (see <see cref="OwnerId"/>'s remarks) - pass the same id
    /// <c>LivePlayerDirectoryChannel</c> handed out for this player.</summary>
    public static async Task<LivePlayerGeneralSession> ConnectAsync(LivePlayerGeneralChannel channel,
        string? playerId = null, string? ownerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var session = new LivePlayerGeneralSession(channel, playerId, ownerId ?? playerId);
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <summary>The connected player's own id (not a SteamID64/XUID - see
    /// <see cref="IsSteamOwnerId"/>, always false here). Shown so the readout still says who this
    /// is, even though it can't be changed live.</summary>
    public string? OwnerId { get; private set; }

    public bool IsSteamOwnerId => false;
    public bool CanChangeOwnerId => false;
    public IPlayerDiscoverySection ItemsSeen { get; }
    public IPlayerDiscoverySection ItemsCrafted { get; }
    public IPlayerDiscoverySection Maps { get; }
    public string? Status { get; private set; }

    /// <summary>The running character's background/PhD row name. See
    /// <see cref="LivePlayerGeneralChannel"/>'s remarks for how this is read and written.</summary>
    public string? Background { get; private set; }

    /// <summary>True once a connected player is resolved - see
    /// <see cref="IPlayerGeneralSession.CanChangeBackground"/>'s remarks for the write path.</summary>
    public bool CanChangeBackground => true;

    public IReadOnlyList<string> Traits => _traits;

    /// <summary>Applies a new background/PhD row name to the running character immediately.</summary>
    public async Task SetBackgroundAsync(string? background)
    {
        if (string.IsNullOrWhiteSpace(background)) return;
        await _channel.SetAsync(background: background, playerId: _playerId).ConfigureAwait(false);
        Background = background;
        Status = "Applied live - this took effect in the running game immediately.";
    }

    /// <summary>Re-reads the live player's known items/maps/traits and background.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        _itemsSeen.Clear(); foreach (var id in directory.ItemsSeen) _itemsSeen.Add(id);
        _itemsCrafted.Clear(); foreach (var id in directory.ItemsCrafted) _itemsCrafted.Add(id);
        _maps.Clear(); foreach (var id in directory.Maps) _maps.Add(id);
        _traits = directory.Traits.ToList();
        Background = directory.Background;
    }

    /// <summary>Switches which connected player this session reads/edits and re-reads that
    /// player's item/map state.</summary>
    public async Task SwitchPlayerAsync(string? playerId, string? ownerId = null, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        OwnerId = ownerId ?? playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        Status = "Refreshed from the running game.";
    }

    private static List<string> CleanNew(IEnumerable<string> vocabulary, HashSet<string> known)
        => vocabulary.Where(id => !string.IsNullOrWhiteSpace(id) && !known.Contains(id))
            .Distinct(StringComparer.Ordinal).ToList();
}
