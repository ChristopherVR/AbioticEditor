namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live journal/codex editing: reads which e-mails, notes, fish and compendium sections the
/// running character's <c>Abiotic_CharacterProgressionComponent_C</c> already knows about, and
/// marks more of them known - the live counterpart to the file editor's <c>PlayerCodexTab</c>
/// ("GATEPal") EMAIL, NOTES, FISH and COMPENDIUM sections. See <c>codex.get</c>/<c>codex.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/codex.lua</c> and
/// docs/reference/live-editing-protocol.md for the wire shape and the pak evidence it is grounded
/// in.
///
/// The COMPENDIUM section (Entities/Locations/IS/People/Theories) is settable here:
/// <c>Request_UnlockCompendiumSection</c> takes an <c>UnlockType</c> enum parameter whose values
/// were previously un-grounded (no working mod calls it with anything but a value read live off a
/// widget). Round 77 grounded it directly from the game's own usmap enum table
/// (<c>ECompendiumUnlockType</c>: Exploration=0, Email=1, NarrativeNPC=2, plus a
/// kill-requirement value and a MAX sentinel) - see <c>areas/codex.lua</c>'s header comment for
/// the full evidence. <see cref="CompendiumUnlock"/> pairs a compendium row with the section type
/// to unlock (a row can need more than one call when its entry spans several section types).
///
/// Round 106: the kill-requirement value is settable too. The round-77 comment above assumed it
/// never was, since no installed mod calls the RPC that way - but the full bytecode of the
/// private function the RPC forwards to disassembles to a 4-case switch on the section type, and
/// case 3 (KilLRequirement) adds the row to `Compendium_KillSections` unconditionally, gated only
/// by the same already-unlocked check the other three share (no kill-count check in this path) -
/// see <c>areas/codex.lua</c>'s header comment for the exact bytecode evidence.
/// <see cref="LiveCodexDirectory.CanUnlockKillSections"/> reports whether this connected agent
/// maps the kill-requirement section type at all (older agents omit the field, treated as false).
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
        return new LiveCodexDirectory(wire.Emails ?? [], wire.Journals ?? [], wire.Fish ?? [], wire.Compendium ?? [],
            wire.CanUnsetKnown, wire.CanUnlockKillSections);
    }

    /// <summary>Marks the given e-mail/journal/fish row names, and/or compendium
    /// (row, section type) pairs, known immediately. Omitted categories are left untouched.</summary>
    public Task SetKnownAsync(IReadOnlyList<string>? emails = null, IReadOnlyList<string>? journals = null,
        IReadOnlyList<string>? fish = null, IReadOnlyList<CompendiumUnlock>? compendium = null,
        string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("codex.set",
            new SetWire(playerId, emails, journals, fish,
                compendium?.Select(c => new CompendiumUnlockWire(c.Row, c.SectionType)).ToList()),
            cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);

    public Task ClearAsync(string section, IReadOnlyList<string> ids, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("codex.set", new { playerId, clear = new { section, ids } }, cancellationToken);

    private sealed record DirectoryWire(
        IReadOnlyList<string>? Emails, IReadOnlyList<string>? Journals,
        IReadOnlyList<string>? Fish, IReadOnlyList<string>? Compendium, bool CanUnsetKnown = false,
        bool CanUnlockKillSections = false);

    private sealed record SetWire(
        string? PlayerId, IReadOnlyList<string>? Emails, IReadOnlyList<string>? Journals,
        IReadOnlyList<string>? Fish, IReadOnlyList<CompendiumUnlockWire>? Compendium);

    private sealed record CompendiumUnlockWire(string Row, string SectionType);
}

/// <summary>A compendium row to unlock plus which section type to unlock it for (one of
/// <c>"Exploration"</c>, <c>"Email"</c>, <c>"NarrativeNPC"</c> - the same names
/// <c>Core/Catalogs/Codex/CodexCatalog.cs</c>'s <c>CompendiumEntry.SectionTypes</c> already uses,
/// translated to the RPC's integer enum value on the Lua side - or, round 106, <c>"KillRequirement"</c>,
/// which the shared catalog never puts in <c>SectionTypes</c> itself - see <c>LivePlayerCodexSession</c>'s
/// <c>BuildCompendiumRows</c> for where that value is added live-only). A row whose entry spans more
/// than one section type needs one pair per section type to fully unlock.</summary>
public readonly record struct CompendiumUnlock(string Row, string SectionType);

/// <summary>Row names the running character currently knows, one list per GATEPal section.
/// <see cref="Compendium"/> is read-only - see <see cref="LivePlayerCodexChannel"/>'s remarks.</summary>
/// <param name="CanUnlockKillSections">Round 106: whether this agent maps
/// <c>Request_UnlockCompendiumSection</c>'s kill-requirement value at all. False on an older
/// agent, which keeps kill-requirement-only compendium rows read-only live.</param>
public sealed record LiveCodexDirectory(
    IReadOnlyList<string> Emails, IReadOnlyList<string> Journals,
    IReadOnlyList<string> Fish, IReadOnlyList<string> Compendium, bool CanUnsetKnown = false,
    bool CanUnlockKillSections = false);
