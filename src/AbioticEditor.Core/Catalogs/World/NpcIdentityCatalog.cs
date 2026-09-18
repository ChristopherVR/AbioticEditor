namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Friendly identity labels for narrative-NPC actor classes (see
/// docs/research-narrative-npcs.md). Most world-save NPC entries are generic holograms
/// or story-NPC hosts; a handful are named characters. The save stores only the actor
/// class id, so these labels are curated reference data - kept in Core so any frontend
/// can identify an NPC the same way.
/// </summary>
public static class NpcIdentityCatalog
{
    // Round 124: CanBeKilled backs the NPCS tab's Dead checkbox (WorldNpcsTab.razor) - it must
    // stay disabled for classes players cannot kill in-game, so the control never implies an
    // action the game doesn't honour. See docs/reference/research/research-narrative-npcs.md:
    // holograms are non-interactive scripted scenes (all 62 fixture holograms were alive; the
    // 5 observed IsDead=true entries were story-scripted ParentBP/Ela removals, never a
    // hologram) and the two Human_TRADER entries are static stand actors, not combatants -
    // neither class has ever been observed dead. Everything else (including the generic
    // Human_ParentBP host and every unrecognised class) defaults to killable/editable, matching
    // the "unknown classes default to editable" rule: this table only turns the control off when
    // there is positive evidence the class cannot die, never as a default-deny.
    // Round 124 follow-up: the owner pointed out "hologram" is jargon players don't know, so
    // every label here is written as a plain, one-line explanation of what the player is actually
    // looking at, not a game-internal class description. These are shown as-is (the row's primary
    // name when no real character name resolved, or the secondary line under one when it did -
    // see WorldNpcsTab.CharacterName/CharacterSecondary) and reused to build the Dead checkbox's
    // disabled tooltip (WorldNpcsTab.CanKillSelected + WorldNpcs_CannotDieTooltipFormat), so one
    // change here updates both places at once.
    private static readonly (string Hint, string Label, bool CanBeKilled)[] _hints =
    {
        ("Human_Hologram", "Recorded projection that plays a scene - not a living character", false),
        ("Human_TRADER", "Trading stall fixed in one spot - not a character you can fight", false),
        ("Human_Killable", "Story character you can actually fight, unlike most on this list", true),
        ("Human_ParentBP", "Placed story character or trader appearance point", true),
        ("Ela_", "Ela - Abe's pet Electro-Pest (Wildlife Pens)", true),
        ("HastaTria", "Hasta Tria - Order soldier (The Mines)", true),
        ("Larva_", "Big Hive Larva - trader", true),
        ("MGT_CKCore", "The Core - Core Keeper crossover", true),
    };

    /// <summary>The default label for an unrecognised narrative NPC.</summary>
    public const string DefaultLabel = "Story NPC";

    /// <summary>
    /// Scans the actor id / name for a known class hint and returns that hint (the stable id
    /// a caller can key a localized label off), or null when none match. See
    /// <see cref="LabelFor"/> for the plain-English label.
    /// </summary>
    public static string? MatchedHint(string id, string actorName)
    {
        foreach (var (hint, _, _) in _hints)
        {
            if (id.Contains(hint, StringComparison.OrdinalIgnoreCase)
                || actorName.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return hint;
            }
        }
        return null;
    }

    /// <summary>
    /// Matches a label by scanning the actor id / name for a known class hint, or returns
    /// <see cref="DefaultLabel"/> when none match.
    /// </summary>
    public static string LabelFor(string id, string actorName)
    {
        if (MatchedHint(id, actorName) is not { } hint) return DefaultLabel;
        foreach (var (h, label, _) in _hints)
        {
            if (h == hint) return label;
        }
        return DefaultLabel;
    }

    /// <summary>
    /// Whether a player can actually kill this narrative NPC in-game, used to gate the NPCS
    /// tab's Dead checkbox. Unrecognised classes default to <c>true</c> (editable) - this table
    /// only says "no" where there is positive evidence (holograms, static trader stands); it
    /// never guesses a class is unkillable just because it is unknown.
    /// </summary>
    public static bool CanBeKilled(string id, string actorName)
    {
        if (MatchedHint(id, actorName) is not { } hint) return true;
        foreach (var (h, _, canBeKilled) in _hints)
        {
            if (h == hint) return canBeKilled;
        }
        return true;
    }
}
