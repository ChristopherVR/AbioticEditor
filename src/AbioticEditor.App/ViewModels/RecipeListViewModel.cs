using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;

namespace AbioticEditor.App.ViewModels;

/// <summary>One recipe row in the browser: definition + mutable unlocked flag.</summary>
public sealed class RecipeRowViewModel : INotifyPropertyChanged
{
    private readonly RecipeListViewModel _owner;
    private bool _isUnlocked;

    public RecipeRowViewModel(RecipeListViewModel owner, RecipeInfo info, ItemCatalogEntry? createdItem, bool unlocked)
    {
        _owner = owner;
        Info = info;
        CreatedItem = createdItem;
        _isUnlocked = unlocked;
    }

    public RecipeInfo Info { get; }
    public ItemCatalogEntry? CreatedItem { get; }

    public string DisplayName => CreatedItem?.DisplayName ?? Info.CreatesItemId ?? Info.Id;
    public string SubText
    {
        get
        {
            var makes = Info.Count > 1 ? $"makes {Info.Count}× · " : string.Empty;
            return $"{Info.Id} · {makes}{Info.Source}";
        }
    }

    /// <summary>Wiki-style detail: the crafted item's in-game description.</summary>
    public string? Description => CreatedItem?.Description;

    // ---------- crafted item icon (lazy, like inventory slots) ----------

    private string? _iconPath;
    private bool _iconRequested;

    public string? IconPath
    {
        get => _iconPath;
        private set
        {
            if (_iconPath == value) return;
            _iconPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowRowIcon)));
        }
    }

    public bool HasIcon => _iconPath is not null;

    /// <summary>Row icon stays hidden while the recipe is sealed (the icon itself spoils it).</summary>
    public bool ShowRowIcon => _iconPath is not null && !IsConcealed;

    /// <summary>
    /// Claims this row's one-time icon request. Returns true exactly once for rows that
    /// have an extractable icon - the caller then owns the extraction.
    /// </summary>
    internal bool TryClaimIconRequest()
    {
        if (_iconRequested || CreatedItem?.IconAssetPath is null) return false;
        _iconRequested = true;
        return true;
    }

    internal void SetIconPath(string path) => IconPath = path;

    public void EnsureIcon()
    {
        if (!TryClaimIconRequest()) return;

        var provider = GameDataServices.Provider;
        if (provider is null) return;

        var entry = CreatedItem!;
        _ = Task.Run(() =>
        {
            try
            {
                var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath!);
                var colorized = raw is null ? null : IconColorizer.Colorize(raw, entry);
                if (colorized is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() => IconPath = colorized);
                }
            }
            catch
            {
                // Icons are cosmetic.
            }
        });
    }

    // ---------- wiki-style detail panel content ----------

    /// <summary>Ingredient lines, e.g. "2× Scrap Metal".</summary>
    public string IngredientsText => Info.IngredientList.Count == 0
        ? "(no ingredients recorded)"
        : string.Join("\n", Info.IngredientList.Select(i =>
            $"{i.Count}× {GameDataServices.Catalog?.Find(i.ItemId)?.DisplayName ?? i.ItemId}"));

    public string BenchesText => Info.BenchList.Count == 0
        ? "Hands (no bench required)"
        : string.Join(", ", Info.BenchList.Select(b => GameDataServices.Catalog?.Find(b)?.DisplayName ?? b));

    public string OutputText => Info.Count > 1 ? $"Makes {Info.Count}×" : "Makes 1×";

    /// <summary>Item stat dump for the detail panel (stack/durability/weight/tags).</summary>
    public string StatsText
    {
        get
        {
            if (CreatedItem is not { } e) return string.Empty;
            var parts = new List<string>();
            if (e.StackSize > 1) parts.Add($"Stack {e.StackSize}");
            if (e.MaxDurability > 0) parts.Add($"Durability {e.MaxDurability:F0}");
            if (e.Weight > 0) parts.Add($"Weight {e.Weight:F1}");
            if (e.IsWeapon) parts.Add("Weapon");
            return string.Join(" · ", parts);
        }
    }

    public string TagsText => CreatedItem is { } e && e.Tags.Count > 0
        ? string.Join("  ", e.Tags)
        : string.Empty;

    public bool HasTags => TagsText.Length > 0;

    /// <summary>What the crafted item is itself an ingredient of.</summary>
    public string UsedInText
    {
        get
        {
            if (Info.CreatesItemId is null) return string.Empty;
            var catalog = GameDataServices.Catalog;
            return string.Join(", ", GameDataServices.RecipesUsing(Info.CreatesItemId)
                .Select(r => catalog?.Find(r.CreatesItemId ?? "")?.DisplayName ?? r.CreatesItemId ?? r.Id)
                .Distinct()
                .Take(14));
        }
    }

    public bool HasUsedIn => UsedInText.Length > 0;

    public string SoldByText => Info.CreatesItemId is null
        ? string.Empty
        : string.Join(", ", GameDataServices.TradersSelling(Info.CreatesItemId)
            .Select(t => TraderLore.NameFor(t.Id)).Distinct());

    public bool HasSoldBy => SoldByText.Length > 0;

    /// <summary>Upgrade path of the crafted item (DT_ItemUpgrades), if any.</summary>
    public string UpgradePathText
    {
        get
        {
            if (Info.CreatesItemId is null) return string.Empty;
            var up = GameDataServices.ItemUpgrades.UpgradeFor(Info.CreatesItemId);
            if (up is null) return string.Empty;
            var catalog = GameDataServices.Catalog;
            var cost = string.Join(", ", up.Required.Select(r =>
                $"{r.Count}× {catalog?.Find(r.ItemId)?.DisplayName ?? r.ItemId}"));
            return $"Upgrades to {catalog?.Find(up.OutputId)?.DisplayName ?? up.OutputId}"
                + (cost.Length > 0 ? $" ({cost})" : string.Empty);
        }
    }

    public bool HasUpgradePath => UpgradePathText.Length > 0;

    public bool IsUnlocked
    {
        get => _isUnlocked;
        set
        {
            if (_isUnlocked == value) return;
            // Progress gate: recipes granted by an email in an unreached region stay
            // locked (the one quest->recipe link the game data encodes).
            if (value && !Services.ProgressContext.CanUnlockRecipe(Info.Id, out var reason))
            {
                Services.ProgressContext.Notify?.Invoke(reason!);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUnlocked)));
                return;
            }
            _isUnlocked = value;
            Core.Diagnostics.EditorLog.Info("Edit", $"Recipe {(value ? "UNLOCKED" : "LOCKED")}: {Info.Id}");
            foreach (var n in new[]
            {
                nameof(IsUnlocked), nameof(StatusLabel), nameof(IsConcealed),
                nameof(ShownName), nameof(ShownStatusLabel), nameof(CanToggle),
                nameof(ShownTooltip), nameof(ShowRowIcon),
            })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            }
            _owner.OnRowToggled();
        }
    }

    public string StatusLabel => _isUnlocked ? "UNLOCKED" : "LOCKED";

    // ---------- spoiler concealment ----------

    /// <summary>Per-item reveal key.</summary>
    public string SpoilerKey => SpoilerService.Key(SpoilerService.Recipe, Info.Id);

    /// <summary>A locked recipe is content the player hasn't unlocked yet (a craftable spoiler).</summary>
    public bool IsConcealed => SpoilerService.ShouldConceal(SpoilerKey, !_isUnlocked);

    public string ShownName => SpoilerService.Mask(DisplayName, IsConcealed);
    public string ShownStatusLabel => IsConcealed ? SpoilerService.ClassifiedShort : StatusLabel;
    public string? ShownTooltip => IsConcealed ? SpoilerService.ClassifiedHint : Description;

    /// <summary>The unlock checkbox is disabled while sealed - reveal first to act on it.</summary>
    public bool CanToggle => !IsConcealed;

    internal void NotifyConcealment()
    {
        foreach (var n in new[]
        {
            nameof(IsConcealed), nameof(ShownName), nameof(ShownStatusLabel), nameof(CanToggle),
            nameof(ShownTooltip), nameof(ShowRowIcon),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>Prompts to override clearance; on confirm reveals this recipe permanently.</summary>
    public async Task RevealAsync()
    {
        if (!IsConcealed) return;
        if (await SpoilerPrompt.RevealAsync("This recipe", SpoilerKey))
        {
            NotifyConcealment();
            _owner.OnRevealed();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Searchable, filterable list over the full recipe vocabulary with per-recipe unlock
/// toggles. Shared by the player editor (RecipesUnlock_) and the world metadata editor
/// (GlobalRecipes*). Unknown save-only rows (renamed/legacy recipes) are preserved
/// verbatim on save.
/// </summary>
/// <summary>One recipe-browser category chip (groups by the crafted item's wiki bucket).</summary>
public sealed record RecipeCategory(string Label, string? Key);

public sealed class RecipeListViewModel : INotifyPropertyChanged
{
    public enum Show { All, Locked, Unlocked }

    private readonly List<RecipeRowViewModel> _rows;
    private readonly List<string> _unknownUnlocked;
    private HashSet<string> _baseline;
    private readonly Action _onChanged;
    private string _searchText = string.Empty;
    private Show _filter = Show.All;
    private IReadOnlyList<RecipeRowViewModel> _visible = Array.Empty<RecipeRowViewModel>();

    public RecipeListViewModel(IEnumerable<string> unlocked, Action onChanged)
    {
        _onChanged = onChanged;

        var unlockedSet = new HashSet<string>(unlocked, StringComparer.Ordinal);
        _baseline = new HashSet<string>(unlockedSet, StringComparer.Ordinal);

        var catalog = GameDataServices.Catalog;
        var infos = GameDataServices.AllRecipeInfos;
        var known = new HashSet<string>(infos.Select(i => i.Id), StringComparer.Ordinal);

        _rows = infos
            .Select(i => new RecipeRowViewModel(
                this, i,
                i.CreatesItemId is null ? null : catalog?.Find(i.CreatesItemId),
                unlockedSet.Contains(i.Id)))
            .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _unknownUnlocked = unlocked.Where(r => !known.Contains(r)).ToList();

        UnlockAllCommand = new RelayCommand(UnlockAll, () => _rows.Any(r => !r.IsUnlocked));
        ApplyFilter();
    }

    public bool IsAvailable => _rows.Count > 0;
    public ICommand UnlockAllCommand { get; }

    // ---------- category chips ----------

    /// <summary>
    /// Wiki-ordered buckets keyed by the crafted item's category, plus the game's two
    /// dedicated recipe tables (soups, chemistry).
    /// </summary>
    public IReadOnlyList<RecipeCategory> Categories { get; } = new[]
    {
        new RecipeCategory("ALL", null),
        new RecipeCategory("WEAPONS", "@weapons"),
        new RecipeCategory("ARMOR", "@armor"),
        new RecipeCategory("TOOLS", "@tools"),
        new RecipeCategory("POWER", "@power"),
        new RecipeCategory("DEFENSE", "@defense"),
        new RecipeCategory("FURNITURE", "@furniture"),
        new RecipeCategory("MEDICAL", "@medical"),
        new RecipeCategory("FOOD", "@food"),
        new RecipeCategory("FARMING", "@farming"),
        new RecipeCategory("RESOURCES", "@resources"),
        new RecipeCategory("SOUPS", "#Soup"),
        new RecipeCategory("CHEMISTRY", "#Chemistry"),
    };

    private RecipeCategory? _selectedCategory;

    public RecipeCategory? SelectedCategory
    {
        get => _selectedCategory ??= Categories[0];
        set
        {
            if (Set(ref _selectedCategory, value)) ApplyFilter();
        }
    }

    public ICommand SelectCategoryCommand => _selectCategoryCommand ??=
        new RelayCommand<RecipeCategory>(c => SelectedCategory = c ?? Categories[0]);
    private RelayCommand<RecipeCategory>? _selectCategoryCommand;

    private bool MatchesCategory(RecipeRowViewModel r)
    {
        var key = SelectedCategory?.Key;
        if (key is null) return true;
        if (key.StartsWith('#')) return string.Equals(r.Info.Source, key[1..], StringComparison.OrdinalIgnoreCase);
        if (r.Info.Source != "Crafting") return false;
        return r.CreatedItem is not null && ItemPaletteViewModel.CategoryOf(r.CreatedItem) == key;
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty)) ApplyFilter();
        }
    }

    public bool ShowAll
    {
        get => _filter == Show.All;
        set { if (value) Filter = Show.All; }
    }

    public bool ShowLocked
    {
        get => _filter == Show.Locked;
        set { if (value) Filter = Show.Locked; }
    }

    public bool ShowUnlocked
    {
        get => _filter == Show.Unlocked;
        set { if (value) Filter = Show.Unlocked; }
    }

    public Show Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                foreach (var n in new[] { nameof(ShowAll), nameof(ShowLocked), nameof(ShowUnlocked) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
                ApplyFilter();
            }
        }
    }

    public IReadOnlyList<RecipeRowViewModel> VisibleRecipes
    {
        get => _visible;
        private set => Set(ref _visible, value);
    }

    private RecipeRowViewModel? _selectedRecipe;

    /// <summary>The recipe whose wiki-style detail panel is shown (single-tap selects).</summary>
    public RecipeRowViewModel? SelectedRecipe
    {
        get => _selectedRecipe;
        set
        {
            // A sealed recipe can't open its detail card; tapping it prompts a reveal
            // instead and the selection is rejected (snaps back to the prior row).
            if (value is { IsConcealed: true })
            {
                _ = value.RevealAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRecipe)));
                return;
            }
            if (Set(ref _selectedRecipe, value))
            {
                value?.EnsureIcon();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedRecipe)));
            }
        }
    }

    /// <summary>Re-filters after a row was individually revealed (it may now sort/show differently).</summary>
    internal void OnRevealed() => ApplyFilter();

    public bool HasSelectedRecipe => _selectedRecipe is not null;

    public int UnlockedCount => _rows.Count(r => r.IsUnlocked) + _unknownUnlocked.Count;
    public int TotalCount => _rows.Count + _unknownUnlocked.Count;
    public string CountText => IsAvailable
        ? $"{UnlockedCount} of {TotalCount} unlocked"
        : "game data unavailable - recipe browser disabled";

    public bool IsDirty
    {
        get
        {
            var current = CurrentSet();
            return current.Count != _baseline.Count || !current.SetEquals(_baseline);
        }
    }

    /// <summary>The full unlocked list to write back into the save.</summary>
    public List<string> CurrentList()
    {
        var result = new List<string>(_unknownUnlocked);
        result.AddRange(GameDataServices.AllRecipeInfos
            .Where(i => _rows.First(r => r.Info.Id == i.Id).IsUnlocked)
            .Select(i => i.Id));
        return result;
    }

    private HashSet<string> CurrentSet()
    {
        var set = new HashSet<string>(_unknownUnlocked, StringComparer.Ordinal);
        foreach (var r in _rows)
        {
            if (r.IsUnlocked) set.Add(r.Info.Id);
        }
        return set;
    }

    public void AcceptCurrentAsBaseline() => _baseline = CurrentSet();

    private bool _suppressToggleNotifications;

    public void Revert()
    {
        _suppressToggleNotifications = true;
        try
        {
            foreach (var r in _rows)
            {
                r.IsUnlocked = _baseline.Contains(r.Info.Id);
            }
        }
        finally
        {
            _suppressToggleNotifications = false;
        }
        NotifyCounts();
        ApplyFilter();
    }

    private void UnlockAll()
    {
        _suppressToggleNotifications = true;
        try
        {
            foreach (var r in _rows) r.IsUnlocked = true;
        }
        finally
        {
            _suppressToggleNotifications = false;
        }
        NotifyCounts();
        _onChanged();
        ApplyFilter();
    }

    internal void OnRowToggled()
    {
        if (_suppressToggleNotifications) return;
        NotifyCounts();
        _onChanged();
    }

    private void NotifyCounts()
    {
        foreach (var n in new[] { nameof(UnlockedCount), nameof(CountText), nameof(IsDirty) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        (UnlockAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ApplyFilter()
    {
        IEnumerable<RecipeRowViewModel> q = _rows.Where(MatchesCategory);
        if (_filter == Show.Locked) q = q.Where(r => !r.IsUnlocked);
        else if (_filter == Show.Unlocked) q = q.Where(r => r.IsUnlocked);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var f = _searchText.Trim();
            q = q.Where(r =>
                r.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.Info.Id.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (r.Info.CreatesItemId?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var visible = q.ToList();
        VisibleRecipes = visible;

        // Lazy icons for the rendered rows. Rows that already requested an icon are
        // skipped; the rest are extracted on ONE background task instead of one
        // Task.Run per row - the first unfiltered pass shows hundreds of rows, and a
        // task per row floods the thread pool.
        RequestIcons(visible);
    }

    private static void RequestIcons(IReadOnlyList<RecipeRowViewModel> rows)
    {
        var provider = GameDataServices.Provider;
        if (provider is null) return;

        var pending = rows.Where(r => r.TryClaimIconRequest()).ToList();
        if (pending.Count == 0) return;

        _ = Task.Run(() =>
        {
            foreach (var r in pending)
            {
                try
                {
                    var entry = r.CreatedItem!;
                    var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath!);
                    var colorized = raw is null ? null : IconColorizer.Colorize(raw, entry);
                    if (colorized is not null)
                    {
                        MainThread.BeginInvokeOnMainThread(() => r.SetIconPath(colorized));
                    }
                }
                catch
                {
                    // Icons are cosmetic.
                }
            }
        });
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
