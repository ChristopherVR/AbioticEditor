using System.ComponentModel;
using System.Runtime.CompilerServices;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;

namespace AbioticEditor.App.ViewModels;

/// <summary>One catalog item in the palette, with a lazily extracted icon.</summary>
public sealed class PaletteItemViewModel : INotifyPropertyChanged
{
    private string? _iconPath;
    private bool _iconRequested;

    public PaletteItemViewModel(ItemCatalogEntry entry)
    {
        Entry = entry;
    }

    public ItemCatalogEntry Entry { get; }
    public string Id => Entry.Id;
    public string DisplayName => Entry.DisplayName;
    public string SubText => $"{Entry.Id} · stack {Entry.StackSize}" + (Entry.MaxDurability > 0 ? $" · dur {Entry.MaxDurability:F0}" : "");

    /// <summary>Wiki-style hover text: name, stats, and the in-game description.</summary>
    public string Tooltip
    {
        get
        {
            var stats = $"stack {Entry.StackSize} · {Entry.Weight:0.##} kg"
                + (Entry.MaxDurability > 0 ? $" · durability {Entry.MaxDurability:F0}" : "");
            return string.IsNullOrWhiteSpace(Entry.Description)
                ? $"{DisplayName}\n{stats}"
                : $"{DisplayName}\n{stats}\n\n{Entry.Description}";
        }
    }

    // ---------- encyclopedia detail (computed when the item is selected) ----------

    public string StatsText
    {
        get
        {
            var parts = new List<string> { $"stack {Entry.StackSize}", $"{Entry.Weight:0.##} kg" };
            if (Entry.MaxDurability > 0) parts.Add($"durability {Entry.MaxDurability:F0}");
            if (Entry.IsWeapon) parts.Add("weapon");
            return string.Join(" · ", parts);
        }
    }

    public string TagsText => Entry.Tags.Count > 0 ? string.Join("  ", Entry.Tags) : string.Empty;

    /// <summary>"CRAFTED BY" - recipes producing this item, with their ingredients.</summary>
    public string CraftedByText
    {
        get
        {
            var catalog = GameDataServices.Catalog;
            var lines = GameDataServices.RecipesCrafting(Id).Select(r =>
            {
                var ingredients = string.Join(", ", r.IngredientList.Select(i =>
                    $"{i.Count}× {catalog?.Find(i.ItemId)?.DisplayName ?? i.ItemId}"));
                var bench = r.BenchList.Count > 0
                    ? $"  [{string.Join(", ", r.BenchList.Select(b => catalog?.Find(b)?.DisplayName ?? b))}]"
                    : string.Empty;
                return ingredients.Length > 0 ? $"• {ingredients}{bench}" : $"• {r.Id}{bench}";
            }).ToList();
            return string.Join("\n", lines);
        }
    }

    public bool HasCraftedBy => GameDataServices.RecipesCrafting(Id).Any();

    /// <summary>"USED IN" - what this item helps craft.</summary>
    public string UsedInText
    {
        get
        {
            var catalog = GameDataServices.Catalog;
            var names = GameDataServices.RecipesUsing(Id)
                .Select(r => catalog?.Find(r.CreatesItemId ?? "")?.DisplayName ?? r.CreatesItemId ?? r.Id)
                .Distinct()
                .Take(14)
                .ToList();
            return string.Join(", ", names);
        }
    }

    public bool HasUsedIn => GameDataServices.RecipesUsing(Id).Any();

    /// <summary>"SOLD BY" - traders that offer this item (barter).</summary>
    public string SoldByText
        => string.Join(", ", GameDataServices.TradersSelling(Id).Select(t => TraderLore.NameFor(t.Id)).Distinct());

    public bool HasSoldBy => GameDataServices.TradersSelling(Id).Any();

    public string? IconPath
    {
        get => _iconPath;
        private set
        {
            if (_iconPath == value) return;
            _iconPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
        }
    }

    public bool HasIcon => _iconPath is not null;

    /// <summary>
    /// Kicks off background icon extraction (once). Safe to call repeatedly; the texture
    /// cache makes repeat extractions cheap.
    /// </summary>
    public void EnsureIcon()
    {
        if (_iconRequested || Entry.IconAssetPath is null) return;
        _iconRequested = true;

        var provider = GameDataServices.Provider;
        if (provider is null) return;

        _ = Task.Run(() =>
        {
            try
            {
                var raw = provider.ExtractTextureByGameRef(Entry.IconAssetPath);
                var colorized = raw is null ? null : IconColorizer.Colorize(raw, Entry);
                if (colorized is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() => IconPath = colorized);
                }
            }
            catch
            {
                // Missing icons are cosmetic.
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One coarse filter bucket in the palette's chip row.</summary>
public sealed record PaletteCategory(string Label, string? TagPrefix);

/// <summary>
/// Searchable view over the full <see cref="ItemCatalog"/>: filter by name/id/tag and a
/// coarse category, then drag results onto inventory slots (or double-tap) to spawn items.
/// </summary>
public sealed class ItemPaletteViewModel : INotifyPropertyChanged
{
    /// <summary>Page size for rendered results - keeps each CollectionView refresh + icon extraction snappy.</summary>
    private const int PageSize = 72;

    private readonly List<PaletteItemViewModel> _all;
    private string _searchText = string.Empty;
    private PaletteCategory _selectedCategory;
    private IReadOnlyList<PaletteItemViewModel> _visibleItems = Array.Empty<PaletteItemViewModel>();
    private List<PaletteItemViewModel> _currentMatches = new();
    private int _visibleCount;
    private int _totalMatches;

    public ItemPaletteViewModel(ItemCatalog catalog)
    {
        // Drop unfinished/test rows (empty or placeholder display names) and sink
        // entries whose display name is just the raw id below properly named items.
        _all = catalog.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName)
                     && e.DisplayName != "?"
                     && !e.DisplayName.Contains("DEPRECATED", StringComparison.OrdinalIgnoreCase)
                     && !e.DisplayName.Contains("DONOTUSE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => string.Equals(e.DisplayName, e.Id, StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => new PaletteItemViewModel(e))
            .ToList();
        _selectedCategory = Categories[0];
        SelectCategoryCommand = new RelayCommand<PaletteCategory>(c => SelectedCategory = c ?? Categories[0]);
        LoadMoreCommand = new RelayCommand(LoadMore);
        ApplyFilter();
    }

    /// <summary>
    /// Wiki-ordered categories (abioticfactor.wiki.gg/wiki/Items). Tag prefixes where the
    /// game tags them; <see cref="CategoryOf"/> adds id-pattern heuristics for the rest.
    /// </summary>
    public IReadOnlyList<PaletteCategory> Categories { get; } = new[]
    {
        new PaletteCategory("ALL", null),
        new PaletteCategory("RESOURCES", "@resources"),
        new PaletteCategory("FURNITURE", "@furniture"),
        new PaletteCategory("TOOLS", "@tools"),
        new PaletteCategory("POWER", "@power"),
        new PaletteCategory("DEFENSE", "@defense"),
        new PaletteCategory("WEAPONS", "@weapons"),
        new PaletteCategory("ARMOR", "@armor"),
        new PaletteCategory("MEDICAL", "@medical"),
        new PaletteCategory("FOOD", "@food"),
        new PaletteCategory("FARMING", "@farming"),
        new PaletteCategory("OTHER", "@other"),
    };

    /// <summary>
    /// Buckets an item into a wiki category using gameplay tags first, then id patterns.
    /// </summary>
    internal static string CategoryOf(ItemCatalogEntry e)
    {
        bool Tag(string prefix) => e.Tags.Any(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        bool IdHas(params string[] hints) => hints.Any(h => e.Id.Contains(h, StringComparison.OrdinalIgnoreCase));

        if (Tag("Item.Ammo") || Tag("Item.Weapon") || IdHas("weapon_", "ammo_", "grenade", "frag", "_gun", "magnum", "shotgun", "rifle", "crossbow", "launcher")) return "@weapons";
        if (Tag("Item.Gear") || IdHas("armor", "helmet", "backpack_", "trinket", "suit_", "goggles", "headlamp", "watch_", "shield")) return "@armor";
        if (IdHas("bandage", "medkit", "syringe", "splint", "pills", "firstaid", "antidote", "vaccine")) return "@medical";
        if (Tag("Item.Food") || IdHas("food_", "soup_", "drink", "coffee", "tea_", "snack", "fish_")) return "@food";
        if (IdHas("seed", "fertilizer", "gardenplot", "wateringcan", "scarecrow", "plant")) return "@farming";
        if (IdHas("trap_", "_trap", "turret", "barricade", "tripwire", "mine_", "noisemaker", "spikes")) return "@defense";
        if (IdHas("battery", "powercell", "brick_power", "lamp", "light_", "flashlight", "glowstick", "generator", "solar", "cable")) return "@power";
        if (IdHas("bench", "furniture", "chair", "table", "bed_", "shelf", "crate", "couch", "locker", "freezer", "fridge", "stove", "oven", "sink_", "toilet")) return "@furniture";
        if (IdHas("tool", "screwdriver", "wrench", "hammer", "drill", "vacuum", "fishingrod", "keypadhacker", "scanner", "extinguisher")) return "@tools";
        if (IdHas("scrap_", "gib_", "essence", "crystal", "alloy", "ore_", "ingot", "plastic", "cloth", "tech_", "circuitboard", "harddrive", "casefan", "powersupply", "glue", "tape", "paper", "rubberband", "spring", "gear_", "coil", "wire", "lens", "diode", "carbon", "gem", "silver", "gold")) return "@resources";
        return "@other";
    }

    public System.Windows.Input.ICommand SelectCategoryCommand { get; }

    /// <summary>Reveals the next page of matches without re-running the search/category filter.</summary>
    public System.Windows.Input.ICommand LoadMoreCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty)) ApplyFilter();
        }
    }

    // ---------- equipment-slot fit filter ----------

    private string? _roleFilter;
    private bool _filterToRole = true;

    /// <summary>
    /// The equipment role of the currently selected slot (HEAD, SHIELD, ...) or null when
    /// a non-equipment slot is active. Set by the shell whenever the active slot changes.
    /// </summary>
    public string? RoleFilter
    {
        get => _roleFilter;
        set
        {
            if (Set(ref _roleFilter, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRoleFilter)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleFilterText)));
                ApplyFilter();
            }
        }
    }

    public bool HasRoleFilter => _roleFilter is not null;

    public string RoleFilterText => _roleFilter is null ? string.Empty : $"FITS {_roleFilter} SLOT";

    /// <summary>When on (default), the palette hides items the selected equipment slot rejects.</summary>
    public bool FilterToRole
    {
        get => _filterToRole;
        set
        {
            if (Set(ref _filterToRole, value)) ApplyFilter();
        }
    }

    public PaletteCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is not null && Set(ref _selectedCategory, value)) ApplyFilter();
        }
    }

    public IReadOnlyList<PaletteItemViewModel> VisibleItems
    {
        get => _visibleItems;
        private set => Set(ref _visibleItems, value);
    }

    /// <summary>The palette row for an item id, or null (case-insensitive).</summary>
    public PaletteItemViewModel? FindById(string itemId)
        => _all.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));

    private PaletteItemViewModel? _selectedItem;

    /// <summary>The item whose encyclopedia detail is shown (single-tap selects).</summary>
    public PaletteItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (Set(ref _selectedItem, value))
            {
                value?.EnsureIcon();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedItem)));
            }
        }
    }

    public bool HasSelectedItem => _selectedItem is not null;

    public int TotalMatches
    {
        get => _totalMatches;
        private set
        {
            if (Set(ref _totalMatches, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchSummary)));
            }
        }
    }

    /// <summary>Whether more matches exist beyond the currently rendered page.</summary>
    public bool HasMore => _visibleCount < _totalMatches;

    public string MatchSummary => _totalMatches <= _visibleCount
        ? $"{_totalMatches} item(s)"
        : $"showing {_visibleCount} of {_totalMatches}";

    private void ApplyFilter()
    {
        IEnumerable<PaletteItemViewModel> q = _all;

        // Equipment-slot fit: same rule the drop handler enforces, applied up front so
        // the palette only offers items the selected slot will accept.
        if (_filterToRole && _roleFilter is { } role)
        {
            q = q.Where(i => InventorySlotViewModel.ValidateForRole(role, i.Entry) is null);
        }

        if (_selectedCategory.TagPrefix is { } prefix)
        {
            q = prefix.StartsWith('@')
                ? q.Where(i => CategoryOf(i.Entry) == prefix)
                : q.Where(i => i.Entry.Tags.Any(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var f = _searchText.Trim();
            q = q.Where(i =>
                i.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                i.Id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                i.Entry.Tags.Any(t => t.Contains(f, StringComparison.OrdinalIgnoreCase)));
        }

        _currentMatches = q.ToList();
        _visibleCount = Math.Min(PageSize, _currentMatches.Count);
        TotalMatches = _currentMatches.Count;
        ShowPage();
    }

    /// <summary>Reveals the next page of the already-computed match list.</summary>
    private void LoadMore()
    {
        if (!HasMore) return;
        _visibleCount = Math.Min(_visibleCount + PageSize, _currentMatches.Count);
        ShowPage();
    }

    private void ShowPage()
    {
        var visible = _currentMatches.Take(_visibleCount).ToList();
        VisibleItems = visible;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMore)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchSummary)));

        foreach (var item in visible)
        {
            item.EnsureIcon();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
