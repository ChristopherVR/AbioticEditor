using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// The ids a host knows about, used to spot content in a save that the host cannot name. Each set
/// is nullable on purpose: null means "this host has no data for that area", so nothing in it is
/// judged. An empty or missing game-data bundle therefore never reports a whole save as modded.
/// </summary>
public sealed record ModdedContentVocabulary(
    IReadOnlySet<string>? Items,
    IReadOnlySet<string>? Recipes,
    IReadOnlySet<string>? QuestLines,
    IReadOnlyDictionary<string, IReadOnlySet<int>>? NativeMutations,
    IReadOnlyDictionary<string, IReadOnlySet<int>>? LegacyMutations)
{
    /// <summary>Builds the vocabulary from a bundled game-data snapshot (the browser build's only data source).</summary>
    public static ModdedContentVocabulary FromRegistry(GameDataRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new(
            Ids(registry.Items?.Select(item => item.Id)),
            Ids(registry.Recipes?.Select(recipe => recipe.Id)),
            Ids(registry.Quests?.Lines.Select(line => line.Id)),
            Mutations(registry.PetMutationOptions),
            Mutations(registry.LegacyPetMutationOptions));
    }

    private static HashSet<string>? Ids(IEnumerable<string>? ids)
    {
        var set = ids?.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set is { Count: > 0 } ? set : null;
    }

    private static Dictionary<string, IReadOnlySet<int>>? Mutations(
        IReadOnlyDictionary<string, IReadOnlyList<PetMutationOption>>? options)
        => options?.ToDictionary(pair => pair.Key,
            pair => (IReadOnlySet<int>)pair.Value.Select(option => option.Value).ToHashSet(),
            StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// What a save holds that the host's game data cannot name. These are kept untouched when the save
/// is written; the report only tells the player what the editor cannot describe.
/// </summary>
public sealed record ModdedContentReport(
    IReadOnlyList<string> Items,
    IReadOnlyList<string> Recipes,
    IReadOnlyList<string> QuestLines,
    int PetMutations)
{
    public static readonly ModdedContentReport None = new([], [], [], 0);

    public int Total => Items.Count + Recipes.Count + QuestLines.Count + PetMutations;
    public bool HasAny => Total > 0;

    public ModdedContentReport Combine(ModdedContentReport other) => new(
        Items.Union(other.Items, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        Recipes.Union(other.Recipes, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        QuestLines.Union(other.QuestLines, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        PetMutations + other.PetMutations);
}

/// <summary>
/// Finds ids in a loaded save that are missing from a host's game data: modded rows, or rows from a
/// game update newer than the bundled data. Read-only; it never changes a save.
/// </summary>
public static class ModdedContentScanner
{
    public static ModdedContentReport ScanPlayer(PlayerSaveData data, ModdedContentVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var slots = data.Inventory.Equipment.Concat(data.Inventory.Hotbar).Concat(data.Inventory.Main).Concat(data.TransmogSlots);
        var native = PlayerQuestSave.Read(data);
        var mutations = native.SupportsMultipleQuests ? vocabulary.NativeMutations : vocabulary.LegacyMutations;

        // A pet whose item the host does not know is already counted as an unknown item, so only
        // pets it does know can have a mutation it cannot name.
        var unknownMutations = 0;
        if (mutations is not null && vocabulary.Items is { } items)
        {
            unknownMutations = data.CarriedPets.Count(pet => pet.PetMutation != 0 && items.Contains(pet.ItemRow)
                && !(mutations.TryGetValue(pet.ItemRow, out var known) && known.Contains(pet.PetMutation)));
        }

        return new(
            Missing(slots.Where(slot => !slot.IsEmpty).Select(slot => slot.ItemId), vocabulary.Items),
            MissingRecipes(data.Recipes, vocabulary),
            Missing(native.TrackedLine is null ? [] : [native.TrackedLine], vocabulary.QuestLines),
            unknownMutations);
    }

    public static ModdedContentReport ScanWorld(
        IEnumerable<WorldContainer> containers, IEnumerable<WorldDroppedItem> droppedItems,
        IEnumerable<string> globalRecipes, ModdedContentVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(droppedItems);
        ArgumentNullException.ThrowIfNull(globalRecipes);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var slots = containers.SelectMany(container => container.Inventories).SelectMany(inventory => inventory.Slots)
            .Concat(droppedItems.Select(item => item.Slot));
        return new(
            Missing(slots.Where(slot => !slot.IsEmpty).Select(slot => slot.ItemId), vocabulary.Items),
            MissingRecipes(globalRecipes, vocabulary), [], 0);
    }

    // A recipe the base game itself retired is not modded content: old unmodded worlds still list it.
    private static string[] MissingRecipes(IEnumerable<string> ids, ModdedContentVocabulary vocabulary)
        => Missing(ids, vocabulary.Recipes).Where(id => !RetiredRecipeCatalog.IsRetired(id, vocabulary.Items)).ToArray();

    private static string[] Missing(IEnumerable<string?> ids, IReadOnlySet<string>? known)
        => known is null ? []
            : ids.Where(id => !string.IsNullOrWhiteSpace(id) && !known.Contains(id!))
                .Select(id => id!).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
}
