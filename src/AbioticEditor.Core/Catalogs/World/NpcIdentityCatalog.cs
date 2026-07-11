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
    private static readonly (string Hint, string Label)[] _hints =
    {
        ("Human_Hologram", "Story hologram (scripted scene, cannot die)"),
        ("Human_TRADER", "Static trader stand"),
        ("Human_Killable", "Killable story NPC"),
        ("Human_ParentBP", "Story NPC / trader host"),
        ("Ela_", "Ela - Abe's pet Electro-Pest (Wildlife Pens)"),
        ("HastaTria", "Hasta Tria - Order soldier (The Mines)"),
        ("Larva_", "Big Hive Larva - trader"),
        ("MGT_CKCore", "The Core - Core Keeper crossover"),
    };

    /// <summary>The default label for an unrecognised narrative NPC.</summary>
    public const string DefaultLabel = "Story NPC";

    /// <summary>
    /// Matches a label by scanning the actor id / name for a known class hint, or returns
    /// <see cref="DefaultLabel"/> when none match.
    /// </summary>
    public static string LabelFor(string id, string actorName)
    {
        foreach (var (hint, label) in _hints)
        {
            if (id.Contains(hint, StringComparison.OrdinalIgnoreCase)
                || actorName.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }
        return DefaultLabel;
    }
}
