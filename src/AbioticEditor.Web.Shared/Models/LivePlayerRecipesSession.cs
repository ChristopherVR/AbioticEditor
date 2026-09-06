using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s recipes slice: implements the
/// same <see cref="IPlayerRecipesSession"/> boundary <c>PlayerRecipesTab</c> already binds to (see
/// <c>IPlayerRecipesSession.cs</c>), so that widget needs zero changes to work against a running
/// game instead of a loaded file. Unlike the file session, an unlock pushes straight to the live
/// game per recipe (<see cref="SetUnlockedAsync"/> is one network round trip), and there is no way
/// to undo it: <see cref="CanLock"/> is always false, matching the running game's own component,
/// which has no lock/relock function anywhere in its exported API (see
/// <c>LivePlayerRecipesChannel</c>'s remarks).
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
        var unlocked = await channel.GetUnlockedAsync(playerId, cancellationToken).ConfigureAwait(false);
        return new LivePlayerRecipesSession(channel, playerId, unlocked);
    }

    public IReadOnlyList<PlayerRecipeEdit> Recipes => _recipes;
    public int UnlockedRecipeCount => _recipes.Count(recipe => recipe.IsUnlocked);
    public int RecipeCount => _recipes.Count;
    public bool AppliesImmediately => true;
    public bool CanLock => false;
    public string? Status { get; private set; }

    public void EnsureRecipeRows(IEnumerable<string> ids)
    {
        var known = _recipes.Select(recipe => recipe.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !known.Add(id)) continue;
            _recipes.Add(new PlayerRecipeEdit(id, _unlockedIds.Contains(id)));
        }
    }

    /// <summary>Unlocks one recipe immediately. Throws when asked to re-lock one - see
    /// <see cref="CanLock"/>'s remarks; the tab disables the checkbox instead of ever calling this
    /// with <paramref name="unlocked"/> = false for an already-unlocked row.</summary>
    public async Task SetUnlockedAsync(string recipeId, bool unlocked)
    {
        if (!unlocked)
        {
            throw new InvalidOperationException(
                "This recipe can't be re-locked while the game is running - there is no game function to do it.");
        }
        await _channel.UnlockAsync([recipeId], _playerId).ConfigureAwait(false);
        _unlockedIds.Add(recipeId);
        var existing = _recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, recipeId, StringComparison.Ordinal));
        if (existing is not null) existing.IsUnlocked = true;
        else _recipes.Add(new PlayerRecipeEdit(recipeId, true));
        Status = "Applied live - this took effect in the running game immediately.";
    }

    /// <summary>No staged-dirty concept live (every write already applied); kept only so callers
    /// that already call it unconditionally (shared with the file session) need no branch.</summary>
    public void MarkChanged() { }

    /// <summary>Re-reads the live player's unlocked recipes, replacing this session's known set
    /// (any locked rows added by <see cref="EnsureRecipeRows"/> for catalog ids are kept).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var unlocked = await _channel.GetUnlockedAsync(_playerId, cancellationToken).ConfigureAwait(false);
        _unlockedIds.Clear();
        foreach (var id in unlocked) _unlockedIds.Add(id);
        var known = _recipes.ToDictionary(recipe => recipe.Id, StringComparer.Ordinal);
        foreach (var id in unlocked)
        {
            if (known.TryGetValue(id, out var edit)) edit.IsUnlocked = true;
            else { var added = new PlayerRecipeEdit(id, true); _recipes.Add(added); known[id] = added; }
        }
        foreach (var edit in _recipes) edit.IsUnlocked = _unlockedIds.Contains(edit.Id);
        Status = "Refreshed from the running game.";
    }

    /// <summary>Switches which connected player this session reads/edits and re-reads that
    /// player's unlocked recipes.</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
