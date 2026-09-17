namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live quest/story flag editing: lists every world flag the game knows (the same row names the
/// file editor's <c>WorldFlags</c> array and <c>QuestFlagCatalog</c> use) with whether each is
/// currently set in the running world, and lets a host set or clear them - see
/// <c>flags.list</c>/<c>flags.set</c> in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c>.
/// The mod side drives the game's own native world-flag subsystem (the object every in-game
/// story trigger, door and effect consults), so a flag flipped here fires the same reactions a
/// player walking into the trigger would.
/// </summary>
public sealed class LiveWorldFlagsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveWorldFlagDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("flags.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveWorldFlagDirectory(
            (wire.Flags ?? []).Select(f => new LiveWorldFlag(f.Name, f.IsSet)).ToList(), wire.IsHost);
    }

    /// <summary>
    /// Sets or clears one or more flags immediately. Returns the names the game did not know
    /// and therefore left alone (a chapter's trigger list can name a flag the game's flag table
    /// no longer carries); empty when every flag applied. An older mod bundle answers with no
    /// body at all, which reads as "nothing skipped".
    /// </summary>
    public async Task<IReadOnlyList<string>> SetAsync(IReadOnlyList<LiveWorldFlag> flags, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<SetResultWire?>("flags.set",
            new SetWire(flags.Select(f => new FlagWire(f.Name, f.IsSet)).ToList()), cancellationToken).ConfigureAwait(false);
        return wire?.Skipped ?? [];
    }

    private sealed record DirectoryWire(IReadOnlyList<FlagWire>? Flags, bool IsHost);
    private sealed record FlagWire(string Name, bool IsSet);
    private sealed record SetWire(IReadOnlyList<FlagWire> Flags);
    private sealed record SetResultWire(IReadOnlyList<string>? Skipped);
}

/// <summary>One world flag: its raw row name and whether the running world has it set.</summary>
public sealed record LiveWorldFlag(string Name, bool IsSet);

/// <summary>Every known flag plus whether this process has host authority to change them.</summary>
public sealed record LiveWorldFlagDirectory(IReadOnlyList<LiveWorldFlag> Flags, bool IsHost);
