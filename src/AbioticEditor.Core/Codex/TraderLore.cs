namespace AbioticEditor.Core.Codex;

/// <summary>
/// Wiki-sourced identity and lore for the DT_NPC_Traders rows
/// (abioticfactor.wiki.gg/wiki/Trading + individual NPC pages). The game tables carry
/// no display names - only row ids and portraits - so this reference data lives in Core
/// where any frontend (editor UI, CLI, future web) can read it.
/// </summary>
public static class TraderLore
{
    /// <param name="Name">Display name.</param>
    /// <param name="Where">Where you first meet them.</param>
    /// <param name="Blurb">Short lore.</param>
    /// <param name="Unlock">
    /// How/when they actually become a trader. The game's <c>DT_NPC_Traders</c> table carries no
    /// world-flag gate for most of them (you unlock them by meeting/helping them in the world or
    /// by finishing the story), so the editor cannot infer this from flags - it is curated here
    /// so the UI never wrongly claims a trader is "available from the start".
    /// </param>
    /// <param name="SpoilerGateFlag">
    /// When set, the spoiler system treats this trader as future content unless the world
    /// has this flag. Required for traders whose <c>DT_NPC_Traders</c> row carries no
    /// <c>RequiredWorldFlags</c> even though they are NOT available from the start (the
    /// game gates them via NPC encounter events or story completion instead of a save flag,
    /// so <see cref="TraderInfo.RequiredFlags"/> is empty and the normal availability
    /// check would always pass).
    /// </param>
    public sealed record Entry(string Name, string Where, string Blurb, string Unlock, string? SpoilerGateFlag = null);

    public static readonly IReadOnlyDictionary<string, Entry> ById = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
    {
        ["Warren"] = new("Warren Bunning",
            "Office Sector plaza - security kiosk (stationary)",
            "Sydney-sider turned subterranean security specialist. Sells security keys, seeds, alloys and repair kits; stock grows with story progress.",
            "The first trader you meet - opens shop early in the Office Sector. Stock expands as the story advances.",
            SpoilerGateFlag: "Office_TalkedToWarren"),
        ["Grayson"] = new("Grayson Isling",
            "Met at the Manufacturing West door; later travels near Repair & Salvage stations",
            "Wounded GATE materials engineer. After you patch him up he sells ammo, hard hats, high-vis vests and repair kits.",
            "Becomes a trader only after you find and heal him at the Manufacturing West door.",
            SpoilerGateFlag: "Grayson_Wandering"),
        ["Blacksmith"] = new("The Blacksmith",
            "Manufacturing West - never leaves the F.O.R.G.E.",
            "Mysterious fabrication expert hired by Dr. Frake; always in a fire-proximity suit. Unlimited material trading.",
            "Available once you reach the F.O.R.G.E. in Manufacturing West.",
            SpoilerGateFlag: "MF_MetBlacksmith"),
        ["Marion"] = new("Marion",
            "Flathill (Rewind Rentals); later travels near Cacophonous Crates",
            "One of the last denizens of Flathill - an Acolyte who worships the Composers. Trades dampeners, hoses, porcelain keys and an antique shotgun.",
            "Opens shop after you reach Flathill and meet her at Rewind Rentals."),
        ["Chef"] = new("Dr. Carson",
            "Security Sector garage; later travels to kitchen areas",
            "Self-proclaimed Culinarian of the Containment Division. Trades cooking gear, seeds, ingredients and beverages.",
            "Becomes a trader after you meet him in the Security Sector garage.",
            SpoilerGateFlag: "Security_Entered"),
        ["Jimmy"] = new("Jimmy Sanders",
            "First met in the Botanical Garden / Botanical Wing",
            "Young New Zealander who took a Taco Mine job in 1992 without asking questions. Trades tacos and fast-food cosmetics.",
            "You first meet him in the Botanical Garden, but he only becomes a trader AFTER you beat the game - then he opens the Taco Mine. NOT available from the start.",
            SpoilerGateFlag: "EndBossDefeated"),
        ["Thule"] = new("Dr. Ulrich Thule",
            "Office cafeteria / Labs atrium / Residence E-Suites",
            "Theoretical physicist, coffee aficionado, committed cynic. Trades a mug of coffee for rare materials.",
            "A post-game trader: only opens shop after you complete the story.",
            SpoilerGateFlag: "EndBossDefeated"),
        ["Larva"] = new("Big Hive Larva",
            "The Hive, in The Encroachment",
            "Larval organism from Anteverse 299. Accepts gem crabs and bait in exchange for figurines and powdered crystal.",
            "Available once you reach The Hive in The Encroachment.",
            SpoilerGateFlag: "Encroachment_Entered"),
    };

    /// <summary>
    /// DT_NPC_Traders rows that are NOT actual traders. Fili (Penny) is an NPC met in
    /// one of the Anteverses - the row exists in the table but she never opens shop,
    /// so the roster hides her. Any other unknown row still renders (future traders).
    /// </summary>
    public static readonly IReadOnlySet<string> NonTraders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Fili" };

    public static string NameFor(string id) => ById.TryGetValue(id, out var e) ? e.Name : id;
}
