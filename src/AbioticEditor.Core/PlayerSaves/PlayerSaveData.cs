using UeSaveGame;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Read-only view of a player save with typed accessors for stats and inventory,
/// alongside the raw <see cref="SaveGame"/> tree the writer mutates and re-serializes.
/// </summary>
public sealed class PlayerSaveData
{
    public PlayerSaveData(
        SaveGame raw,
        CharacterStats stats,
        PlayerInventory inventory,
        IReadOnlyList<PlayerSkill> skills,
        IReadOnlyList<string> traits,
        string? phd,
        LimbHealth health,
        IReadOnlyList<string> recipes,
        IReadOnlyList<string> emailsRead,
        IReadOnlyList<string> journals,
        IReadOnlyList<string> compendiumEmail,
        IReadOnlyList<string> compendiumNarrative,
        IReadOnlyList<string> compendiumExploration,
        IReadOnlyList<string> itemsPickedUp,
        IReadOnlyList<string> craftedItems,
        IReadOnlyList<string> mapsUnlocked,
        IReadOnlyList<KillCount> killCounts,
        IReadOnlyList<string> fishCaught,
        IReadOnlyList<InventoryItemSlot>? transmogSlots = null,
        IReadOnlyList<bool>? transmogVisibility = null,
        double respawnX = 0,
        double respawnY = 0,
        double respawnZ = 0,
        string? respawnLevelGuid = null,
        string? terminalRespawnId = null)
    {
        KillCounts = killCounts;
        FishCaught = fishCaught;
        TransmogSlots = transmogSlots ?? Array.Empty<InventoryItemSlot>();
        TransmogVisibility = transmogVisibility ?? Array.Empty<bool>();
        RespawnX = respawnX;
        RespawnY = respawnY;
        RespawnZ = respawnZ;
        RespawnLevelGuid = respawnLevelGuid;
        TerminalRespawnId = terminalRespawnId;
        Raw = raw;
        Stats = stats;
        Inventory = inventory;
        Skills = skills;
        Traits = traits;
        Phd = phd;
        Health = health;
        Recipes = recipes;
        EmailsRead = emailsRead;
        Journals = journals;
        CompendiumEmail = compendiumEmail;
        CompendiumNarrative = compendiumNarrative;
        CompendiumExploration = compendiumExploration;
        ItemsPickedUp = itemsPickedUp;
        CraftedItems = craftedItems;
        MapsUnlocked = mapsUnlocked;
    }

    public SaveGame Raw { get; }

    /// <summary>
    /// The ABF_SAVE_VERSION from the save's custom header, or null when the save class
    /// wasn't one of our registered <c>[SaveClassPath]</c> classes. Known-good value:
    /// <see cref="SaveClasses.SaveCompatibility.KnownGoodCharacterVersion"/>.
    /// </summary>
    public int? AbfVersion => (Raw.CustomSaveClass as SaveClasses.AbioticCharacterSave)?.Version;

    /// <summary>True when the save class mapped to a registered custom save class.</summary>
    public bool HasKnownSaveClass => Raw.CustomSaveClass is not null;

    /// <summary>The raw save class string from the file header.</summary>
    public string? SaveClassName => Raw.SaveClass?.Value;

    public CharacterStats Stats { get; }
    public PlayerInventory Inventory { get; }

    /// <summary>Positional skill entries; see <see cref="SkillCatalog"/> for identity.</summary>
    public IReadOnlyList<PlayerSkill> Skills { get; }

    /// <summary>Internal trait row names, e.g. <c>Trait_LeadBelly</c>.</summary>
    public IReadOnlyList<string> Traits { get; }

    /// <summary>Background/job row name, e.g. <c>PhD_HumanBio</c> (null when absent).</summary>
    public string? Phd { get; }

    /// <summary>Per-limb health from <c>CharacterHealth_</c>.</summary>
    public LimbHealth Health { get; }

    /// <summary>Unlocked recipe row names from <c>RecipesUnlock_</c>.</summary>
    public IReadOnlyList<string> Recipes { get; }

    /// <summary>Read email row names from <c>EmailsRead_</c>.</summary>
    public IReadOnlyList<string> EmailsRead { get; }

    /// <summary>Discovered journal row names from <c>JournalEntries_</c>.</summary>
    public IReadOnlyList<string> Journals { get; }

    /// <summary>Rows of <c>Compendium_EmailSections_</c> (email-unlock compendium sections).</summary>
    public IReadOnlyList<string> CompendiumEmail { get; }

    /// <summary>Rows of <c>Compendium_NarrativeSections_</c>.</summary>
    public IReadOnlyList<string> CompendiumNarrative { get; }

    /// <summary>Rows of <c>Compendium_ExplorationSections_</c>.</summary>
    public IReadOnlyList<string> CompendiumExploration { get; }

    /// <summary>Union of the three compendium section lists.</summary>
    public IReadOnlyList<string> CompendiumUnlocked =>
        CompendiumEmail.Concat(CompendiumNarrative).Concat(CompendiumExploration)
            .Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Item row names ever picked up (<c>ItemsPickedUp_</c>) - drives "NEW" badges.</summary>
    public IReadOnlyList<string> ItemsPickedUp { get; }

    /// <summary>Item row names ever crafted (<c>CraftedItems_</c>).</summary>
    public IReadOnlyList<string> CraftedItems { get; }

    /// <summary>Unlocked map pamphlets (<c>MapsUnlocked_</c>); rows of DT_MapPamphlets.</summary>
    public IReadOnlyList<string> MapsUnlocked { get; }

    /// <summary>Per-entity kill tallies (<c>Compendium_KillCount_</c>).</summary>
    public IReadOnlyList<KillCount> KillCounts { get; }

    /// <summary>Caught fish rows (<c>Compendium_Fish_</c>); rows of DT_Fish.</summary>
    public IReadOnlyList<string> FishCaught { get; }

    /// <summary>
    /// The 6 <c>TransmogInventory_</c> slots - the cosmetic item shown over each
    /// armor-capable equipment slot. Only <see cref="InventoryItemSlot.ItemId"/> matters
    /// for appearance; the rest is the standard slot payload. Empty when the save has no
    /// transmog array (older saves).
    /// </summary>
    public IReadOnlyList<InventoryItemSlot> TransmogSlots { get; }

    /// <summary>The 12 <c>TransmogVisibility_</c> per-slot "show armor piece" flags.</summary>
    public IReadOnlyList<bool> TransmogVisibility { get; }

    /// <summary>X of <c>LastSafeWorldLocation_</c> - the respawn/load-in position.</summary>
    public double RespawnX { get; }

    /// <summary>Y of <c>LastSafeWorldLocation_</c>.</summary>
    public double RespawnY { get; }

    /// <summary>Z of <c>LastSafeWorldLocation_</c>.</summary>
    public double RespawnZ { get; }

    /// <summary>
    /// <c>LastSafeWorldGUID_</c>: the level the respawn location belongs to. Matches the
    /// <c>LevelGUID</c> top-level property of a <c>WorldSave_*.sav</c>.
    /// </summary>
    public string? RespawnLevelGuid { get; }

    /// <summary>
    /// <c>TerminalRespawnID_</c>: opaque GUID FName of the respawn terminal the player
    /// registered at (a static actor baked into the cooked level - not a save object).
    /// </summary>
    public string? TerminalRespawnId { get; }
}

/// <summary>
/// One <c>CompendiumKillCount</c> struct: a DT_Compendium row + how many the player
/// has killed. The save only carries entries for entities killed at least once.
/// </summary>
public sealed record KillCount(string CompendiumRow, int Count);
