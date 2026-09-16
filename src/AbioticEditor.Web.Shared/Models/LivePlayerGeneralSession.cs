using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Immediate live discovery/background editing. Crafted-item discovery is enabled
/// by the host agent capability. Owner identity is save-only; traits require the native buff capability.
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
        Appearance = new LivePlayerAppearanceSession(channel.Appearance, () => _playerId);
        OwnerId = ownerId;

        ItemsSeen = new DelegateDiscoverySection(() => _itemsSeen, canDiscoverAll: true,
            async vocabulary =>
            {
                var ids = CleanNew(vocabulary, _itemsSeen);
                if (ids.Count == 0) return;
                await _channel.SetAsync(itemsSeen: ids, playerId: _playerId).ConfigureAwait(false);
                foreach (var id in ids) _itemsSeen.Add(id);
                Status = null;
                Changed?.Invoke();
            });
        ItemsCrafted = new DelegateDiscoverySection(() => _itemsCrafted, canDiscoverAll: false,
            _ => throw new InvalidOperationException(
                "Crafted-item discovery requires an updated agent with host authority."));
        Maps = new DelegateDiscoverySection(() => _maps, canDiscoverAll: true,
            async vocabulary =>
            {
                var ids = CleanNew(vocabulary, _maps);
                if (ids.Count == 0) return;
                await _channel.SetAsync(maps: ids, playerId: _playerId).ConfigureAwait(false);
                foreach (var id in ids) _maps.Add(id);
                Status = null;
                Changed?.Invoke();
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
    public IPlayerDiscoverySection ItemsCrafted { get; private set; }
    public IPlayerDiscoverySection Maps { get; }
    public string? Status { get; private set; }

    /// <summary>The running character's background/PhD row name. See
    /// <see cref="LivePlayerGeneralChannel"/>'s remarks for how this is read and written.</summary>
    public string? Background { get; private set; }

    /// <summary>True once a connected player is resolved - see
    /// <see cref="IPlayerGeneralSession.CanChangeBackground"/>'s remarks for the write path.</summary>
    public bool CanChangeBackground => true;

    public IReadOnlyList<string> Traits => _traits;
    public IPlayerAppearanceSession Appearance { get; }

    /// <summary>False here: nothing is staged client-side, every mutation (background, discover)
    /// already reached the running game by the time its awaiting call returns - the same
    /// "applies immediately" rule every other live-editing area follows. This is what the
    /// periodic live refresh loop checks before calling <see cref="RefreshAsync"/> so a refresh
    /// never clobbers an edit still in flight.</summary>
    public bool IsDirty => false;

    public bool CanEditTraits { get; private set; }

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the running character, and after
    /// every mutation below applies - lets a bound UI (the GENERAL and CHARACTER tabs) redraw
    /// without polling this object itself.</summary>
    public event Action? Changed;

    /// <summary>Applies a new background/PhD row name to the running character immediately.</summary>
    public async Task SetBackgroundAsync(string? background)
    {
        if (string.IsNullOrWhiteSpace(background)) return;
        await _channel.SetAsync(background: background, playerId: _playerId).ConfigureAwait(false);
        Background = background;
        Status = null;
        Changed?.Invoke();
    }

    public async Task SetTraitAsync(string id, bool enabled, string? buffRowName = null)
    {
        if (!CanEditTraits) throw new InvalidOperationException("Trait editing requires an updated host agent.");
        if (buffRowName is null) throw new InvalidOperationException("Read the installed game's trait details before editing this trait.");
        await _channel.SetTraitAsync(id, enabled, buffRowName, _playerId).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        Status = null;
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
        CanEditTraits = directory.CanEditTraits;
        if (ItemsCrafted.CanDiscoverAll != directory.CanDiscoverCrafted)
            ItemsCrafted = new DelegateDiscoverySection(() => _itemsCrafted, directory.CanDiscoverCrafted, DiscoverCraftedAsync);
        Changed?.Invoke();
    }

    private async Task DiscoverCraftedAsync(IEnumerable<string> vocabulary)
    {
        if (!ItemsCrafted.CanDiscoverAll) throw new InvalidOperationException("Crafted-item discovery requires an updated agent with host authority.");
        var ids = CleanNew(vocabulary, _itemsCrafted);
        if (ids.Count == 0) return;
        await _channel.DiscoverCraftedAsync(ids, _playerId).ConfigureAwait(false);
        foreach (var id in ids) _itemsCrafted.Add(id);
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Switches which connected player this session reads/edits and re-reads that
    /// player's item/map state.</summary>
    public async Task SwitchPlayerAsync(string? playerId, string? ownerId = null, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        OwnerId = ownerId ?? playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        Status = null;
    }

    private static List<string> CleanNew(IEnumerable<string> vocabulary, HashSet<string> known)
        => vocabulary.Where(id => !string.IsNullOrWhiteSpace(id) && !known.Contains(id))
            .Distinct(StringComparer.Ordinal).ToList();
}
