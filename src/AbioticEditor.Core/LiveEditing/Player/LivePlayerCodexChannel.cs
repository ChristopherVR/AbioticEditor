namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live journal/codex editing: reads which e-mails, notes and fish the running character's
/// <c>Abiotic_CharacterProgressionComponent_C</c> already knows about, and marks more of them
/// known - the live counterpart to the file editor's <c>PlayerCodexTab</c> ("GATEPal") EMAIL,
/// NOTES and FISH sections. See <c>codex.get</c>/<c>codex.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/codex.lua</c> and
/// docs/reference/live-editing-protocol.md for the wire shape and the pak evidence it is grounded
/// in.
///
/// The COMPENDIUM section (Entities/Locations/IS/People/Theories) is read-only here:
/// <c>Request_UnlockCompendiumSection</c> takes an <c>UnlockType</c> enum parameter whose values
/// this project has not been able to ground (no working mod calls it with anything but a value
/// read live off a widget, and the pak dump does not carry enum value names) - guessing them
/// would risk silently unlocking the wrong section or corrupting that enum property, so
/// <see cref="LiveCodexDirectory.Compendium"/> is reported but <see cref="SetKnownAsync"/> does
/// not accept it.
/// </summary>
public sealed class LivePlayerCodexChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveCodexDirectory> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<DirectoryWire>("codex.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return new LiveCodexDirectory(wire.Emails ?? [], wire.Journals ?? [], wire.Fish ?? [], wire.Compendium ?? []);
    }

    /// <summary>Marks the given e-mail/journal/fish row names known immediately. Omitted
    /// categories are left untouched; compendium ids are not accepted (see type remarks).</summary>
    public Task SetKnownAsync(IReadOnlyList<string>? emails = null, IReadOnlyList<string>? journals = null,
        IReadOnlyList<string>? fish = null, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("codex.set", new SetWire(playerId, emails, journals, fish), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);

    private sealed record DirectoryWire(
        IReadOnlyList<string>? Emails, IReadOnlyList<string>? Journals,
        IReadOnlyList<string>? Fish, IReadOnlyList<string>? Compendium);

    private sealed record SetWire(
        string? PlayerId, IReadOnlyList<string>? Emails, IReadOnlyList<string>? Journals, IReadOnlyList<string>? Fish);
}

/// <summary>Row names the running character currently knows, one list per GATEPal section.
/// <see cref="Compendium"/> is read-only - see <see cref="LivePlayerCodexChannel"/>'s remarks.</summary>
public sealed record LiveCodexDirectory(
    IReadOnlyList<string> Emails, IReadOnlyList<string> Journals,
    IReadOnlyList<string> Fish, IReadOnlyList<string> Compendium);
