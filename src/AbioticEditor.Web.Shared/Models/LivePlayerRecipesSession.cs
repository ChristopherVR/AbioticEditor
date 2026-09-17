using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Immediate live recipe editing. Unlocks use the game RPC; relocking is available
/// only when the agent reports host-side array mutation support.
/// </summary>
public sealed class LivePlayerRecipesSession : IPlayerRecipesSession
{
    private readonly LivePlayerRecipesChannel _channel;
    private readonly List<PlayerRecipeEdit> _recipes = [];
    private readonly HashSet<string> _unlockedIds = new(StringComparer.Ordinal);
    private string? _playerId;

    private LivePlayerRecipesSession(LivePlayerRecipesChannel channel, string? playerId, IReadOnlyList<string> unlockedIds)
    {
        _channel = channel;
        _playerId = playerId;
        foreach (var id in unlockedIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !_unlockedIds.Add(id)) continue;
            _recipes.Add(new PlayerRecipeEdit(id, true));
        }
    }

    /// <summary>Connects and reads the recipes currently unlocked for <paramref name="playerId"/>
    /// (or the local player when omitted) to seed the session. The tab's own
    /// <see cref="EnsureRecipeRows"/> call adds locked rows for every recipe the installed game's
    /// vocabulary knows once that loads, exactly as it does for the file session.</summary>
    public static async Task<LivePlayerRecipesSession> ConnectAsync(
        LivePlayerRecipesChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return new LivePlayerRecipesSession(channel, playerId, directory.UnlockedIds) { CanLock = directory.CanLock };
    }

    public IReadOnlyList<PlayerRecipeEdit> Recipes => _recipes;
    public int UnlockedRecipeCount => _recipes.Count(recipe => recipe.IsUnlocked);
    public int RecipeCount => _recipes.Count;
    public bool AppliesImmediately => true;
    public bool CanLock { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Always false: an unlock already reached the running game by the time it
    /// returns, so there is never a client-side staged copy.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> and after every unlock.</summary>
    public event Action? Changed;

    public void EnsureRecipeRows(IEnumerable<string> ids)
    {
        var known = _recipes.Select(recipe => recipe.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !known.Add(id)) continue;
            _recipes.Add(new PlayerRecipeEdit(id, _unlockedIds.Contains(id)));
        }
    }

    /// <summary>Changes one recipe immediately. Relocking requires the host agent capability.</summary>
    public async Task SetUnlockedAsync(string recipeId, bool unlocked)
    {
        if (!unlocked)
        {
            if (!CanLock) throw new InvalidOperationException("Relocking recipes requires an updated agent with host authority.");
            await _channel.LockAsync([recipeId], _playerId).ConfigureAwait(false);
            _unlockedIds.Remove(recipeId);
            var row = _recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, recipeId, StringComparison.Ordinal));
            if (row is not null) row.IsUnlocked = false;
            Status = null;
            Changed?.Invoke();
            return;
        }
        await _channel.UnlockAsync([recipeId], _playerId).ConfigureAwait(false);
        _unlockedIds.Add(recipeId);
        var existing = _recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, recipeId, StringComparison.Ordinal));
        if (existing is not null) existing.IsUnlocked = true;
        else _recipes.Add(new PlayerRecipeEdit(recipeId, true));
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Unlocks every given recipe in one network round trip instead of one per recipe -
    /// <c>recipes.set</c> already accepted a batch of ids (see <see cref="LivePlayerRecipesChannel.UnlockAsync"/>),
    /// but PlayerRecipesTab's UNLOCK ALL used to call <see cref="SetUnlockedAsync"/> once per row
    /// anyway, so a fresh character's few hundred recipes meant a few hundred sequential
    /// round trips through the file-mailbox/game-thread relay - see
    /// <see cref="IPlayerRecipesSession.SetUnlockedManyAsync"/>'s remarks for what that looked
    /// like from the player's side.</summary>
    public async Task SetUnlockedManyAsync(IEnumerable<string> recipeIds)
    {
        var ids = recipeIds.Where(id => !string.IsNullOrWhiteSpace(id) && !_unlockedIds.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return;
        await _channel.UnlockAsync(ids, _playerId).ConfigureAwait(false);
        var byId = _recipes.ToDictionary(recipe => recipe.Id, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            _unlockedIds.Add(id);
            if (byId.TryGetValue(id, out var existing)) existing.IsUnlocked = true;
            else _recipes.Add(new PlayerRecipeEdit(id, true));
        }
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>No staged-dirty concept live (every write already applied); kept only so callers
    /// that already call it unconditionally (shared with the file session) need no branch.</summary>
    public void MarkChanged() { }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a recipe unlocked in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>Re-reads the live player's unlocked recipes, replacing this session's known set
    /// (any locked rows added by <see cref="EnsureRecipeRows"/> for catalog ids are kept).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        var unlocked = directory.UnlockedIds;
        CanLock = directory.CanLock;
        _unlockedIds.Clear();
        foreach (var id in unlocked) _unlockedIds.Add(id);
        var known = _recipes.ToDictionary(recipe => recipe.Id, StringComparer.Ordinal);
        foreach (var id in unlocked)
        {
            if (known.TryGetValue(id, out var edit)) edit.IsUnlocked = true;
            else { var added = new PlayerRecipeEdit(id, true); _recipes.Add(added); known[id] = added; }
        }
        foreach (var edit in _recipes) edit.IsUnlocked = _unlockedIds.Contains(edit.Id);
        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Switches which connected player this session reads/edits and re-reads that
    /// player's unlocked recipes.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
