using AbioticEditor.Core.Codex;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open player-codex ("GATEPal") editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>PlayerCodexTab.razor</c> uses, extracted from
/// <see cref="PlayerSaveSession"/>'s existing codex slice, so that widget binds to either the
/// file-backed session or <c>LivePlayerCodexSession</c> with no changes beyond its parameter's
/// declared type.
/// </summary>
public interface IPlayerCodexSession
{
    IReadOnlyList<CodexRowEdit> Emails { get; }
    IReadOnlyList<CodexRowEdit> Journals { get; }

    /// <summary>Every row's <see cref="CodexRowEdit.Editable"/> is false on
    /// <c>LivePlayerCodexSession</c> - the running game's compendium-unlock function needs an
    /// enum parameter this project could not ground (see <c>LivePlayerCodexChannel</c>'s
    /// remarks), so this section is shown but never live-editable.</summary>
    IReadOnlyList<CodexRowEdit> Compendium { get; }
    IReadOnlyList<CodexRowEdit> Fish { get; }

    /// <summary>False for the file session (edits stage until Save); true live (a "mark known"
    /// RPC fires immediately in the running game).</summary>
    bool AppliesImmediately { get; }

    /// <summary>False live: the running game has no function to un-know an e-mail/note/fish once
    /// known, the same one-directional limit <see cref="IPlayerRecipesSession.CanLock"/>
    /// documents for recipes. The tab disables un-checking an already-known row when this is
    /// false instead of silently no-opping the click.</summary>
    bool CanUnsetKnown { get; }

    bool ApplyCodexVocabulary(CodexVocabulary vocabulary, Func<string, object?[], string>? localize = null);

    /// <summary>Marks (or, when <see cref="CanUnsetKnown"/>, unmarks) one row known - refused for
    /// a row whose <see cref="CodexRowEdit.Editable"/> is false (the COMPENDIUM section live).</summary>
    Task SetKnownAsync(CodexRowEdit row, bool known);

    void MarkChanged();
}
