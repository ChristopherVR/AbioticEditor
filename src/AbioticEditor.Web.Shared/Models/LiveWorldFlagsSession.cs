using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live quest/story flag session: implements the same <see cref="IWorldFlagsSession"/> boundary
/// the <c>WorldFlagsTab</c> widget already uses for the file editor, over
/// <see cref="LiveWorldFlagsChannel"/>. Every edit applies immediately (<c>flags.set</c>) and the
/// session then re-reads the world - the game's own triggers can set flags at any moment, so the
/// list is never trusted from before the write.
/// </summary>
public sealed class LiveWorldFlagsSession : IWorldFlagsSession
{
    private readonly LiveWorldFlagsChannel _channel;
    private LiveWorldFlagDirectory _directory;

    private LiveWorldFlagsSession(LiveWorldFlagsChannel channel, LiveWorldFlagDirectory directory)
    {
        _channel = channel;
        _directory = directory;
    }

    public static async Task<LiveWorldFlagsSession> ConnectAsync(
        LiveWorldFlagsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveWorldFlagsSession(channel, directory);
    }

    public IReadOnlySet<string> Flags => _directory.Flags.Where(f => f.IsSet).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
    public bool CanEditFlags => true;
    public bool AppliesImmediately => true;
    public bool IsHost => _directory.IsHost;
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SetFlagAsync(string flag, bool enabled, CancellationToken cancellationToken = default)
        => ApplyAsync([new LiveWorldFlag(flag, enabled)], cancellationToken);

    public async Task<bool> AddFlagAsync(string? flag, CancellationToken cancellationToken = default)
    {
        var trimmed = flag?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        await SetFlagAsync(trimmed, true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Sends the flag plus every unmet prerequisite in one request - the same offer the
    /// file editor's WorldFlagsTab already made before it became shared with this session.</summary>
    public Task EnableFlagWithPrerequisitesAsync(string flag, CancellationToken cancellationToken = default)
    {
        var set = Flags;
        var edits = FlagGate.PrerequisitesFor(flag).Where(p => !set.Contains(p))
            .Select(p => new LiveWorldFlag(p, true))
            .Append(new LiveWorldFlag(flag, true))
            .ToList();
        return ApplyAsync(edits, cancellationToken);
    }

    public Task ClearFlagWithDependentsAsync(string flag, CancellationToken cancellationToken = default)
    {
        var dependents = FlagGate.DependentsOf([flag], Flags);
        return ApplyAsync(dependents.Select(d => new LiveWorldFlag(d, false)).ToList(), cancellationToken);
    }

    /// <summary>Sets or clears the given flags in one request, then re-reads the world.</summary>
    public async Task ApplyAsync(IReadOnlyList<LiveWorldFlag> edits, CancellationToken cancellationToken = default)
    {
        if (edits.Count == 0) return;
        await _channel.SetAsync(edits, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
