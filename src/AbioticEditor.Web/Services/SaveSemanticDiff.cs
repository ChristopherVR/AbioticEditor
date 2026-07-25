using System.Globalization;

using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>One named thing that differs (a recipe, fish, item, trait...). <see cref="ItemId"/> is
/// the item-catalog id to resolve an icon through <c>ItemIcon</c>, or null when the category has
/// no icon (quest flags, journals, maps...).</summary>
public sealed record SemanticItem(string Id, string DisplayName, string? ItemId);

/// <summary>A scalar that changed between the two saves (money, a skill level...).</summary>
public sealed record SemanticScalar(string Label, string A, string B);

/// <summary>One comparison category (Recipes, Fish, Progression...).</summary>
public sealed class SemanticSection
{
    public required string Title { get; init; }
    public List<SemanticScalar> Scalars { get; } = [];
    public List<SemanticItem> OnlyA { get; } = [];
    public List<SemanticItem> OnlyB { get; } = [];

    public bool HasContent => Scalars.Count > 0 || OnlyA.Count > 0 || OnlyB.Count > 0;

    public string Summary(HostLanguageService languages) => Scalars.Count > 0 && OnlyA.Count == 0 && OnlyB.Count == 0
        ? languages.Resource("Diff_ValuesChanged", Scalars.Count)
        : languages.Resource("Diff_OnlyInAB", OnlyA.Count, OnlyB.Count);
}

/// <summary>
/// Builds a human-readable, domain-aware diff of two saves of the same kind - "save A has fish
/// X, save B doesn't" rather than raw property paths - reusing the editor's catalogs for display
/// names and item icons. The raw property diff (<see cref="AbioticEditor.Core.Compare.SaveDiff"/>)
/// stays available as a deep-dive alongside this. Mirrors the retired native app's
/// <c>Views/SaveSemanticDiff.cs</c>, ported to run without any MAUI/App-singleton dependency.
/// </summary>
public sealed class SaveSemanticDiff(
    ItemCatalogService items,
    RecipeVocabularyService recipes,
    CodexVocabularyService codex,
    HostLanguageService languages)
{
    public List<SemanticSection> BuildPlayer(PlayerSaveData a, PlayerSaveData b)
    {
        var sections = new List<SemanticSection>();

        SemanticItem ByItem(string id) => new(id, items.Find(id)?.DisplayName ?? Pretty(id), id);

        SemanticItem ByRecipe(string id)
        {
            var recipe = recipes.GetRecipeInfos().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            var itemId = recipe?.CreatesItemId;
            var name = itemId is not null ? items.Find(itemId)?.DisplayName : null;
            return new SemanticItem(id, name ?? Pretty(id), itemId);
        }

        SemanticItem ByFish(string id)
        {
            var fish = codex.Get().Fish.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
            var itemId = fish?.ItemId;
            var name = itemId is not null ? items.Find(itemId)?.DisplayName : null;
            return new SemanticItem(id, name ?? Pretty(id), itemId);
        }

        SemanticItem ByTrait(string id) => new(id, TraitCatalog.DisplayNameFor(id), null);
        SemanticItem Plain(string id) => new(id, Pretty(id), null);

        // ----- PROGRESSION: money + per-skill level -----
        var progression = new SemanticSection { Title = languages.Resource("Compare_Progression") };
        if (a.Stats.Money != b.Stats.Money)
        {
            progression.Scalars.Add(new SemanticScalar(languages.Resource("Compare_Money"),
                a.Stats.Money.ToString("N0", CultureInfo.CurrentCulture),
                b.Stats.Money.ToString("N0", CultureInfo.CurrentCulture)));
        }
        var skillDefs = SkillCatalog.Fallback;
        var skillCount = Math.Min(a.Skills.Count, b.Skills.Count);
        for (var i = 0; i < skillCount; i++)
        {
            var la = a.Skills[i].Level;
            var lb = b.Skills[i].Level;
            if (la != lb)
            {
                var name = i < skillDefs.Count ? skillDefs[i].DisplayName : languages.Resource("Compare_SkillNumber", i + 1);
                progression.Scalars.Add(new SemanticScalar(name,
                    languages.Resource("Diff_Level", la), languages.Resource("Diff_Level", lb)));
            }
        }
        AddIf(sections, progression);

        // ----- INVENTORY: slot-by-slot (Equipment / Hotbar / Main), matched by index -----
        var inventory = new SemanticSection { Title = languages.Resource("Compare_Inventory") };
        AddInventoryDiff(inventory, languages.Resource("PlayerInventory_Equipment"), a.Inventory.Equipment, b.Inventory.Equipment, ByItem);
        AddInventoryDiff(inventory, languages.Resource("PlayerInventory_Hotbar"), a.Inventory.Hotbar, b.Inventory.Hotbar, ByItem);
        AddInventoryDiff(inventory, languages.Resource("PlayerInventory_Backpack"), a.Inventory.Main, b.Inventory.Main, ByItem);
        AddIf(sections, inventory);

        // ----- set-difference categories -----
        sections.Add(SetSection(languages.Resource("Compare_RecipesUnlocked"), a.Recipes, b.Recipes, ByRecipe));
        sections.Add(SetSection(languages.Resource("Compare_FishCaught"), a.FishCaught, b.FishCaught, ByFish));
        sections.Add(SetSection(languages.Resource("Compare_Traits"), a.Traits, b.Traits, ByTrait));
        sections.Add(SetSection(languages.Resource("Compare_ItemsDiscovered"), a.ItemsPickedUp, b.ItemsPickedUp, ByItem));
        sections.Add(SetSection(languages.Resource("Compare_ItemsCrafted"), a.CraftedItems, b.CraftedItems, ByItem));
        sections.Add(SetSection(languages.Resource("Compare_MapsUnlocked"), a.MapsUnlocked, b.MapsUnlocked, Plain));
        sections.Add(SetSection(languages.Resource("Compare_JournalEntries"), a.Journals, b.Journals, Plain));
        sections.Add(SetSection(languages.Resource("Compare_EmailsRead"), a.EmailsRead, b.EmailsRead, Plain));

        return sections.Where(s => s.HasContent).ToList();
    }

    public List<SemanticSection> BuildWorld(WorldSaveData a, WorldSaveData b)
    {
        var sections = new List<SemanticSection>();

        SemanticItem ByItem(string id) => new(id, items.Find(id)?.DisplayName ?? Pretty(id), id);

        SemanticItem ByRecipe(string id)
        {
            var recipe = recipes.GetRecipeInfos().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            var itemId = recipe?.CreatesItemId;
            var name = itemId is not null ? items.Find(itemId)?.DisplayName : null;
            return new SemanticItem(id, name ?? Pretty(id), itemId);
        }

        SemanticItem ByFlag(string raw)
        {
            var info = QuestFlagCatalog.Lookup(raw);
            var name = string.IsNullOrEmpty(info.FriendlyName) ? Pretty(raw) : info.FriendlyName;
            return new SemanticItem(raw, name, null);
        }

        // ----- PROGRESSION (metadata saves carry these) -----
        var progression = new SemanticSection { Title = languages.Resource("Compare_Progression") };
        if (!string.Equals(a.StoryProgressionRow, b.StoryProgressionRow, StringComparison.OrdinalIgnoreCase)
            && (a.StoryProgressionRow is not null || b.StoryProgressionRow is not null))
        {
            progression.Scalars.Add(new SemanticScalar(languages.Resource("Compare_StoryChapter"),
                ChapterName(a.StoryProgressionRow), ChapterName(b.StoryProgressionRow)));
        }
        if (a.MinutesPassed is { } ma && b.MinutesPassed is { } mb && ma != mb)
        {
            progression.Scalars.Add(new SemanticScalar(languages.Resource("Compare_TimePlayed"),
                languages.Resource("Compare_TimePlayedFormat", ma / 60, ma % 60),
                languages.Resource("Compare_TimePlayedFormat", mb / 60, mb % 60)));
        }
        if (progression.HasContent) sections.Add(progression);

        sections.Add(SetSection(languages.Resource("Compare_GlobalRecipes"), a.GlobalRecipes, b.GlobalRecipes, ByRecipe));
        sections.Add(SetSection(languages.Resource("Compare_QuestFlags"), a.Flags, b.Flags, ByFlag));

        // ----- doors that changed lock/open state (matched by id) -----
        var doors = new SemanticSection { Title = languages.Resource("Compare_Doors") };
        var doorsB = b.Doors.ToDictionary(d => d.Id, d => d, StringComparer.OrdinalIgnoreCase);
        foreach (var da in a.Doors)
        {
            if (!doorsB.TryGetValue(da.Id, out var db)) continue;
            var sa = DoorState(da);
            var sb = DoorState(db);
            if (!string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
            {
                doors.Scalars.Add(new SemanticScalar(Short(da.Id), sa, sb));
            }
        }
        if (doors.HasContent) sections.Add(doors);

        // ----- ground items present in one save but not the other (aggregated by item) -----
        sections.Add(SetSection(languages.Resource("Compare_GroundItems"),
            a.DroppedItems.Select(d => d.Slot.ItemId).Where(NotEmpty).ToList()!,
            b.DroppedItems.Select(d => d.Slot.ItemId).Where(NotEmpty).ToList()!,
            ByItem));

        // ----- NPCs whose state changed (matched by id) -----
        var npcs = new SemanticSection { Title = languages.Resource("Compare_Npcs") };
        var npcsB = b.Npcs.ToDictionary(n => n.Id, n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var na in a.Npcs)
        {
            if (!npcsB.TryGetValue(na.Id, out var nb)) continue;
            var sa = NpcState(na);
            var sb = NpcState(nb);
            if (!string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
            {
                npcs.Scalars.Add(new SemanticScalar(na.ActorName, sa, sb));
            }
        }
        if (npcs.HasContent) sections.Add(npcs);

        // ----- world-object counts -----
        var contents = new SemanticSection { Title = languages.Resource("Compare_WorldContents") };
        AddCountScalar(contents, languages.Resource("Compare_Containers"), a.Containers.Count, b.Containers.Count);
        AddCountScalar(contents, languages.Resource("Compare_PlacedObjects"), a.Deployables.Count, b.Deployables.Count);
        AddCountScalar(contents, languages.Resource("Compare_GroundItems"), a.DroppedItems.Count, b.DroppedItems.Count);
        AddCountScalar(contents, languages.Resource("Compare_TrackedNpcs"), a.Npcs.Count, b.Npcs.Count);
        if (contents.HasContent) sections.Add(contents);

        return sections.Where(s => s.HasContent).ToList();
    }

    private static bool NotEmpty(string? id) => !string.IsNullOrEmpty(id) && id is not ("None" or "Empty");

    private static void AddCountScalar(SemanticSection s, string label, int a, int b)
    {
        if (a != b) s.Scalars.Add(new SemanticScalar(label, a.ToString(CultureInfo.InvariantCulture), b.ToString(CultureInfo.InvariantCulture)));
    }

    private string ChapterName(string? row)
        => row is null
            ? languages.Resource("Diff_None")
            : StoryProgressionCatalog.Find(row) is { } chapter
                ? languages.Resource($"WorldStory_ChapterTitle_{chapter.Row}")
                : row;

    private static string DoorState(WorldDoor d)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(d.DoorState)) parts.Add(DoorStateNames.Friendly(d.DoorState));
        if (d.OneWayUnlocked == true) parts.Add("one-way unlocked");
        if (d.IsDoorOpen == true) parts.Add("open");
        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    private string NpcState(WorldNpc n)
        => n.IsDead ? languages.Resource("Diff_NpcDead") : string.IsNullOrEmpty(n.State) ? languages.Resource("Diff_NpcAlive") : n.State!;

    private static string Short(string id)
    {
        var dot = id.LastIndexOf('.');
        return dot >= 0 ? id[(dot + 1)..] : id;
    }

    private static void AddIf(List<SemanticSection> sections, SemanticSection s)
    {
        if (s.HasContent) sections.Add(s);
    }

    /// <summary>
    /// Adds one scalar per slot index that differs between the two inventory arrays, phrased in
    /// human terms - e.g. "Hotbar slot 3: Pistol (12 ammo) -> Pistol (6 ammo)". Slots that match
    /// (same item, count, ammo, durability) are skipped; an emptied or filled slot shows "(empty)"
    /// on the relevant side.
    /// </summary>
    private void AddInventoryDiff(
        SemanticSection section,
        string area,
        IReadOnlyList<InventoryItemSlot> a,
        IReadOnlyList<InventoryItemSlot> b,
        Func<string, SemanticItem> byItem)
    {
        var count = Math.Max(a.Count, b.Count);
        for (var i = 0; i < count; i++)
        {
            var sa = i < a.Count ? a[i] : null;
            var sb = i < b.Count ? b[i] : null;

            var ta = SlotText(sa, byItem);
            var tb = SlotText(sb, byItem);
            if (string.Equals(ta, tb, StringComparison.Ordinal)) continue;

            section.Scalars.Add(new SemanticScalar(languages.Resource("Compare_SlotNumber", area, i + 1), ta, tb));
        }
    }

    private string SlotText(InventoryItemSlot? slot, Func<string, SemanticItem> byItem)
    {
        if (slot is null || slot.IsEmpty) return languages.Resource("Diff_Empty");

        var name = byItem(slot.ItemId!).DisplayName;
        var details = new List<string>();
        if (slot.Count > 1) details.Add($"x{slot.Count}");
        if (slot.AmmoInMagazine > 0) details.Add(languages.Resource("Diff_Ammo", slot.AmmoInMagazine));
        if (slot.MaxDurability > 0)
        {
            details.Add(languages.Resource("Diff_Durability", Math.Round(slot.DurabilityPercent * 100)));
        }
        if (slot.LiquidLevel > 0 && !string.IsNullOrEmpty(slot.LiquidType))
        {
            details.Add($"{slot.LiquidLevel} {Pretty(slot.LiquidType!)}");
        }

        return details.Count > 0 ? $"{name} ({string.Join(", ", details)})" : name;
    }

    private static SemanticSection SetSection(
        string title, IReadOnlyList<string> a, IReadOnlyList<string> b, Func<string, SemanticItem> resolve)
    {
        var section = new SemanticSection { Title = title };
        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        section.OnlyA.AddRange(a.Where(x => !setB.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resolve).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        section.OnlyB.AddRange(b.Where(x => !setA.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resolve).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        return section;
    }

    /// <summary>Fallback display name for an unresolved id: "recipe_first_aid" -> "first aid".</summary>
    private static string Pretty(string id)
    {
        var s = id;
        foreach (var prefix in new[] { "recipe_", "srecipe_", "crecipe_", "Trait_" })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { s = s[prefix.Length..]; break; }
        }
        return s.Replace('_', ' ').Trim();
    }
}
