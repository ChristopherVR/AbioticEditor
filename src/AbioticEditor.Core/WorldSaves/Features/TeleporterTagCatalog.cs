namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// The built-in Teleporter Pad "tags" (a.k.a. frequencies) the game lets a player pick from to
/// link pads together - two pads on the same tag form a network. A pad stores its choice as an
/// integer <c>TeleporterFrequency</c> dynamic property: <c>0</c> = unassigned, and <c>1..133</c>
/// = the 133 named tags below (134 selectable values in total, matching the wiki's count).
///
/// <para><b>Order</b> is the in-game frequency index, recovered from the
/// <see href="https://abioticfactor.wiki.gg/wiki/Teleporter_Pad">Teleporter Pad wiki table</see>
/// read column-major (its columns are coherent groups: NATO A-M, NATO N-Z, region names, damage
/// types, …) and <b>verified against real save data</b> - every tagged pad in the test fixtures
/// resolves to a sensible region name (27→Facility, 34→The Reactors, 122→Far Garden, 133→Voussoir).
/// If a future patch reorders the list, only this array needs updating.</para>
/// </summary>
public static class TeleporterTagCatalog
{
    /// <summary>The label shown for the unassigned frequency (index 0).</summary>
    public const string None = "(none)";

    /// <summary>The 133 named tags, in game frequency order (this[0] is frequency 1).</summary>
    private static readonly string[] Names =
    {
        // --- NATO phonetic A-M (frequencies 1-13) ---
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India",
        "Juliett", "Kilo", "Lima", "Mike",
        // --- NATO phonetic N-Z (14-26) ---
        "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor",
        "Whiskey", "X-Ray", "Yankee", "Zulu",
        // --- regions (27-39) ---
        "Facility", "The Office Sector", "Manufacturing West", "Cascade Laboratories",
        "Security Sector", "Hydroplant", "Power Services", "The Reactors", "Residence Sector",
        "The Blacksmith", "Warren", "Kizz", "Acid",
        // --- damage types (40-52) ---
        "Blunt", "Sharp", "Bullet", "Cold", "Electricity", "Explosive", "Fire", "'Holy'", "Laser",
        "Plasma", "Poison", "Psychic", "Witch's Cauldron",
        // --- substances (53-65) ---
        "Cooked", "Window", "Shutters", "Reactor Conduit", "Blood", "Antejuice", "Fuel", "Ink",
        "Soup", "Tainted Water", "Vomit", "Feces", "Ammo",
        // --- objects / stations (66-78) ---
        "Crafting Bench", "Bench", "Book", "Charred Remains", "Quantum Exchanger",
        "Interesting Diagram", "Deployable", "Coworker", "Fish", "Rare Fish", "Food",
        "Fungal Bloom", "GATE Charging Station",
        // --- gear / fixtures (79-91) ---
        "GATE Pal", "Gear", "Grenade", "Hatch", "Cupboard", "Locker", "Medical", "Odd Contraption",
        "Binder", "Plant", "Retinal Scanner", "Tailgate", "Database Terminal",
        // --- terminals / machines (92-104) ---
        "Email Terminal", "Toilet", "Tool", "Coffee Machine", "Drinks Vending Machine",
        "Snacks Vending Machine", "Slushie Machine", "Weapon", "Strange Webbing", "Zipline", "Dead",
        "Fertilized", "Flowering",
        // --- plants / vehicles (105-117) ---
        "Harvestable", "Juvenile", "Regrowing", "Seedling", "Sprout", "Rare", "Forklift",
        "GATE Security Cart", "SUV", "Insufficient Money.", "Out of Stock.", "Ordo Storage",
        "Vehicle Depot",
        // --- places (118-130) ---
        "Staff Processing", "The Mines", "Fragments", "Synaptic Labs", "Far Garden",
        "The Mycofields", "Some Distant Shore", "Flathill", "The Train", "Furniture Store",
        "The Night Realm", "Rise", "Canaan",
        // --- remainder (131-133) ---
        "The World in the Mirror", "Shadowgate", "Voussoir",
    };

    /// <summary>The highest valid frequency value (= number of named tags).</summary>
    public static int MaxFrequency => Names.Length;

    /// <summary>
    /// All selectable labels for a tag picker: <see cref="None"/> at position 0 then the 133
    /// named tags, so the list index equals the frequency value.
    /// </summary>
    public static IReadOnlyList<string> Choices { get; } =
        new[] { None }.Concat(Names).ToArray();

    /// <summary>
    /// The tag label for a frequency value: <see cref="None"/> for 0, the name for 1..133, or
    /// <c>"Tag #N"</c> for an out-of-range value (so an unknown future tag still round-trips).
    /// </summary>
    public static string Label(int frequency)
    {
        if (frequency == 0)
        {
            return None;
        }
        if (frequency >= 1 && frequency <= Names.Length)
        {
            return Names[frequency - 1];
        }
        return $"Tag #{frequency.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The frequency value for a tag label (case-insensitive; <see cref="None"/> → 0, or a
    /// <c>"Tag #N"</c> form → N). Returns null when the label isn't a known tag.
    /// </summary>
    public static int? Frequency(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }
        var trimmed = label.Trim();
        if (string.Equals(trimmed, None, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        for (var i = 0; i < Names.Length; i++)
        {
            if (string.Equals(Names[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }
        if (trimmed.StartsWith("Tag #", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(trimmed[5..], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }
        return null;
    }
}
