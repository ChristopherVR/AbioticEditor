using System.ComponentModel;
using System.Runtime.CompilerServices;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Mutable view-model for one inventory slot. Bound to slot tiles + the slot detail editor.
/// Mutations fire <see cref="INotifyPropertyChanged"/> so the parent editor can mark itself
/// dirty.
/// </summary>
public sealed class InventorySlotViewModel : INotifyPropertyChanged
{
    public InventorySlotViewModel(
        InventoryKind kind,
        InventoryItemSlot slot,
        ItemCatalogEntry? entry,
        string? iconPath,
        string? role = null)
    {
        Kind = kind;
        Index = slot.Index;
        Role = role;
        _iconPath = iconPath;
        _entry = entry;

        _itemId = slot.ItemId;
        _count = slot.Count;
        _durability = slot.Durability;
        _maxDurability = slot.MaxDurability;
        _ammoInMagazine = slot.AmmoInMagazine;
        _liquidLevel = slot.LiquidLevel;
        _liquidType = slot.LiquidType;
        _dynamicState = slot.DynamicState;
        _playerMadeString = slot.PlayerMadeString;
        _assetId = slot.AssetId;

        OriginalSlot = slot;
    }

    public InventoryKind Kind { get; }
    public int Index { get; }
    public string? Role { get; }

    private bool _isSelected;

    /// <summary>
    /// True while this slot is the one open in the slot editor. Maintained by
    /// MainViewModel.ActiveSlot; drives the highlight border in the slot templates.
    /// </summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private string? _iconPath;
    private ItemCatalogEntry? _entry;
    private bool _iconRequested;
    public string? IconPath { get => _iconPath; private set => Set(ref _iconPath, value); }
    public ItemCatalogEntry? Entry { get => _entry; private set => Set(ref _entry, value); }

    /// <summary>
    /// Kicks off background icon extraction (idempotent). Icons are never loaded on the
    /// UI thread - a fresh editor with dozens of occupied slots stays responsive and the
    /// images pop in as the cache fills.
    /// </summary>
    public void EnsureIcon()
    {
        if (_iconRequested || IsEmpty) return;
        _iconRequested = true;

        var entry = _entry;
        var provider = GameDataServices.Provider;
        if (entry?.IconAssetPath is null || provider is null) return;

        _ = Task.Run(() =>
        {
            try
            {
                var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath);
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

    /// <summary>The slot's values at load time - used to detect edits.</summary>
    public InventoryItemSlot OriginalSlot { get; private set; }

    private string? _itemId;
    private int _count;
    private double _durability;
    private double _maxDurability;
    private int _ammoInMagazine;
    private int _liquidLevel;
    private string? _liquidType;
    private bool _dynamicState;
    private string? _playerMadeString;
    private string? _assetId;

    public string? ItemId
    {
        get => _itemId;
        set
        {
            if (Set(ref _itemId, value))
            {
                // Refresh catalog entry + icon to match the new item id.
                RefreshDerivedFromItemId();
            }
        }
    }

    private void RefreshDerivedFromItemId()
    {
        var catalog = GameDataServices.Catalog;
        Entry = (IsEmpty || catalog is null) ? null : catalog.Find(_itemId);
        IconPath = null;
        _iconRequested = false;
        _liquidOptionsCache = null;
        EnsureIcon();
    }
    public int Count { get => _count; set => Set(ref _count, value); }
    public double Durability { get => _durability; set => Set(ref _durability, value); }
    public double MaxDurability { get => _maxDurability; set => Set(ref _maxDurability, value); }
    public int AmmoInMagazine { get => _ammoInMagazine; set => Set(ref _ammoInMagazine, value); }
    public int LiquidLevel { get => _liquidLevel; set => Set(ref _liquidLevel, value); }
    public string? LiquidType
    {
        get => _liquidType;
        set
        {
            if (Set(ref _liquidType, value))
            {
                // An out-of-row liquid is appended to the options; rebuild on change.
                _liquidOptionsCache = null;
            }
        }
    }
    public bool DynamicState { get => _dynamicState; set => Set(ref _dynamicState, value); }
    public string? PlayerMadeString { get => _playerMadeString; set => Set(ref _playerMadeString, value); }
    public string? AssetId { get => _assetId; set => Set(ref _assetId, value); }

    public bool IsEmpty => string.IsNullOrEmpty(_itemId) || _itemId is "None" or "Empty";
    public bool HasIcon => IconPath is not null && !IsEmpty;
    public bool HasRole => !string.IsNullOrEmpty(Role);
    public bool IsStackable => (Entry?.StackSize ?? 1) > 1;
    public int MaxStackSize => Entry?.StackSize ?? 1;

    /// <summary>
    /// True only for firearms and ammo-using items - not melee weapons or tools.
    /// AF tags all weapons (including drills, knives, clubs) with <c>Item.Weapon</c>, so
    /// we look for id patterns that indicate a magazine-using firearm or note when ammo
    /// is already loaded in the save.
    /// </summary>
    public bool HasAmmoCapacity
    {
        get
        {
            if (_ammoInMagazine > 0) return true;
            if (string.IsNullOrEmpty(_itemId)) return false;
            return MatchesAnyFirearmPrefix(_itemId);
        }
    }

    private static readonly string[] FirearmIdHints =
    {
        "magnum", "shotgun", "rifle", "pistol", "crossbow", "nailgun",
        "netlauncher", "sniper", "carbine", "smg", "lmg",
        "_gun", "gun_", "heavy_laser", "disc_launcher", "flintlock",
        "machine", "minigun", "blunderbuss",
    };

    private static bool MatchesAnyFirearmPrefix(string itemId)
    {
        var lower = itemId.ToLowerInvariant();
        foreach (var hint in FirearmIdHints)
        {
            if (lower.Contains(hint, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Equipment slot type validation: enum-first, comparing the item row's
    /// <c>EquipSlot</c> (E_InventorySlotType) against the slot role's expected type.
    /// See <see cref="EquipSlotTypes"/> and dotnet/docs/research-slot-types.md.
    /// </summary>
    public string? ValidationWarning
        => IsEmpty || !HasRole ? null : ValidateForRole(Role, Entry);

    /// <summary>
    /// Prospective slot-fit check, usable BEFORE assigning an item (drop validation).
    /// Returns a human-readable problem or null when the item fits.
    /// </summary>
    public static string? ValidateForRole(string? Role, ItemCatalogEntry? Entry)
        => EquipSlotTypes.ValidateForRole(Role, Entry);

    public bool HasValidationWarning => ValidationWarning is not null;

    // ---------- item upgrades (DT_ItemUpgrades: source + required items -> output) ----------

    /// <summary>The game-defined upgrade applied to this item, or null when not upgradable.</summary>
    public ItemUpgrade? NextUpgrade => IsEmpty
        ? null
        : GameDataServices.ItemUpgrades.UpgradeFor(_itemId) ?? ChainUpgradeFor(_itemId);

    /// <summary>The upgrade that produced this item (downgrade target), or null for base tiers.</summary>
    public ItemUpgrade? PreviousUpgrade => IsEmpty
        ? null
        : GameDataServices.ItemUpgrades.SourceOf(_itemId) ?? ChainSourceOf(_itemId);

    // Tier-family STEMS not in DT_ItemUpgrades (the game upgrades them through bespoke
    // gameplay, e.g. hacking higher-tier keypads). Chains are built from the live
    // catalog - base id, then "<stem>_t2".."<stem>_t9" while the rows exist - so a game
    // update adding keypad_hacker_t7 is picked up with no code change.
    private static readonly string[] SpecialChainStems = { "keypad_hacker" };

    private static IReadOnlyList<string[]>? _specialChains;

    private static IReadOnlyList<string[]> SpecialUpgradeChains
    {
        get
        {
            if (_specialChains is not null) return _specialChains;
            var catalog = GameDataServices.Catalog;
            if (catalog is null) return Array.Empty<string[]>(); // don't cache before the catalog loads

            var chains = new List<string[]>();
            foreach (var stem in SpecialChainStems)
            {
                if (catalog.Find(stem) is null) continue;
                var chain = new List<string> { stem };
                for (var t = 2; t <= 9; t++)
                {
                    var id = $"{stem}_t{t}";
                    if (catalog.Find(id) is null) break;
                    chain.Add(id);
                }
                if (chain.Count > 1) chains.Add(chain.ToArray());
            }
            return _specialChains = chains;
        }
    }

    private static ItemUpgrade? ChainUpgradeFor(string? itemId)
        => FindInChains(itemId, out var chain, out var i) && i < chain.Length - 1
            ? new ItemUpgrade(chain[i], chain[i + 1], Array.Empty<RecipeIngredient>())
            : null;

    private static ItemUpgrade? ChainSourceOf(string? itemId)
        => FindInChains(itemId, out var chain, out var i) && i > 0
            ? new ItemUpgrade(chain[i - 1], chain[i], Array.Empty<RecipeIngredient>())
            : null;

    private static bool FindInChains(string? itemId, out string[] chain, out int index)
    {
        if (itemId is not null)
        {
            foreach (var c in SpecialUpgradeChains)
            {
                var i = Array.FindIndex(c, id => id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                if (i >= 0)
                {
                    chain = c;
                    index = i;
                    return true;
                }
            }
        }
        chain = Array.Empty<string>();
        index = -1;
        return false;
    }

    public bool CanUpgrade => NextUpgrade is not null;
    public bool CanDowngrade => PreviousUpgrade is not null;

    /// <summary>Drives the in-game-style "upgradable" badge on item tiles.</summary>
    public bool IsUpgradable => CanUpgrade;

    /// <summary>
    /// Dismantling replaces the item with its crafting-recipe ingredients - only
    /// possible when such a recipe exists (same rule the salvage flow enforces).
    /// </summary>
    public bool CanDismantle => !IsEmpty
        && _itemId is not null
        && GameDataServices.RecipesCrafting(_itemId).FirstOrDefault()?.IngredientList.Count > 0;

    // ---------- liquids (per-item allowed types + capacity from LiquidData) ----------

    /// <summary>The item holds liquids (water bottles, pots, the teleporter's charge...).</summary>
    public bool HasLiquidCapacity => (Entry?.MaxLiquid ?? 0) > 0;

    public int MaxLiquid => Entry?.MaxLiquid ?? 0;

    public string LiquidCapacityText => $"0–{MaxLiquid}";

    private IReadOnlyList<LiquidOption>? _liquidOptionsCache;

    /// <summary>
    /// Only the liquids this item's row allows, plus Empty to drain it. Cached: the
    /// WinUI Picker resets its platform items every time ItemsSource changes identity,
    /// and a fresh list per read sends Picker.UpdateSelectedIndex and the platform
    /// SelectionChanged handler into a feedback loop that pins the UI thread (the
    /// transmog/drop hang). Invalidated when the item or its liquid changes.
    /// </summary>
    public IReadOnlyList<LiquidOption> LiquidOptions
    {
        get
        {
            if (_liquidOptionsCache is not null) return _liquidOptionsCache;

            var allowed = Entry?.AllowedLiquidList ?? Array.Empty<int>();
            var options = LiquidTypes.All
                .Where(l => l.Enumerator == 0 || allowed.Contains(l.Enumerator))
                .ToList();
            // The save may carry a liquid the row no longer allows - keep it selectable.
            var current = LiquidTypes.ParseSaveValue(_liquidType);
            if (current > 0 && options.All(o => o.Enumerator != current))
            {
                options.Add(new LiquidOption(current, LiquidTypes.NameFor(current)));
            }
            return _liquidOptionsCache = options;
        }
    }

    public LiquidOption? SelectedLiquid
    {
        get
        {
            var current = LiquidTypes.ParseSaveValue(_liquidType);
            return LiquidOptions.FirstOrDefault(o => o.Enumerator == Math.Max(0, current));
        }
        set
        {
            // Write-back guard: the platform Picker re-raises SelectionChanged while
            // its items are being swapped; only a real change may touch the slot.
            if (value is null || value.SaveValue == _liquidType) return;
            LiquidType = value.SaveValue;
            if (value.Enumerator == 0) LiquidLevel = 0;
        }
    }

    /// <summary>This item came out of an upgrade (tinted tile background, like the game).</summary>
    public bool IsUpgraded => PreviousUpgrade is not null;

    public string UpgradeText => NextUpgrade is { } up
        ? $"→ {GameDataServices.Catalog?.Find(up.OutputId)?.DisplayName ?? up.OutputId}"
        : string.Empty;

    /// <summary>"Bench cost" line for the upgrade button, e.g. "1× Carbon Plating, 4× Leather Scraps".</summary>
    public string UpgradeRequirementText => NextUpgrade is { } up && up.Required.Count > 0
        ? "Needs: " + string.Join(", ", up.Required.Select(r =>
            $"{r.Count}× {GameDataServices.Catalog?.Find(r.ItemId)?.DisplayName ?? r.ItemId}"))
        : string.Empty;

    public bool HasUpgradeRequirements => UpgradeRequirementText.Length > 0;

    /// <summary>Swap to the upgraded item (full durability, like the upgrade bench does).</summary>
    public void Upgrade()
    {
        if (NextUpgrade is { } up) SwapTo(up.OutputId);
    }

    public void Downgrade()
    {
        if (PreviousUpgrade is { } up) SwapTo(up.SourceId);
    }

    private void SwapTo(string targetId)
    {
        ItemId = targetId;
        var entry = GameDataServices.Catalog?.Find(targetId);
        if (entry is not null && entry.MaxDurability > 0)
        {
            MaxDurability = entry.MaxDurability;
            Durability = entry.MaxDurability;
        }
        OnDependentsChanged();
    }

    public string DisplayName => !IsEmpty
        ? (Entry?.DisplayName ?? _itemId ?? "(unknown)")
        : (Role ?? string.Empty);

    public bool ShowRolePlaceholder => IsEmpty && HasRole && !HasIcon;
    public string CountText => _count > 1
        ? _count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;
    public bool ShowCount => _count > 1;
    public double DurabilityPercent => _maxDurability > 0
        ? Math.Clamp(_durability / _maxDurability, 0, 1)
        : 0;
    public bool ShowDurability => _maxDurability > 0 && !IsEmpty;
    public string DurabilityText => $"{_durability:F0}/{_maxDurability:F0}";

    /// <summary>Has the user mutated this slot away from its loaded values?</summary>
    public bool IsDirty =>
        _itemId != OriginalSlot.ItemId ||
        _count != OriginalSlot.Count ||
        // Tolerant: the durability slider quantizes its write-back when a tab
        // re-realizes it; fractional drift on an untouched slot is not an edit.
        Math.Abs(_durability - OriginalSlot.Durability) > 0.5 ||
        Math.Abs(_maxDurability - OriginalSlot.MaxDurability) > 0.5 ||
        _ammoInMagazine != OriginalSlot.AmmoInMagazine ||
        _liquidLevel != OriginalSlot.LiquidLevel ||
        _liquidType != OriginalSlot.LiquidType ||
        _dynamicState != OriginalSlot.DynamicState ||
        _playerMadeString != OriginalSlot.PlayerMadeString;

    /// <summary>Build a new InventoryItemSlot record reflecting the VM's current values.</summary>
    public InventoryItemSlot ToCurrentSlot() => new(
        Index,
        _itemId,
        _count,
        _durability,
        _maxDurability,
        _ammoInMagazine,
        _liquidLevel,
        _liquidType,
        _dynamicState,
        _playerMadeString,
        _assetId);

    /// <summary>Snapshot the current values as the new baseline (called after a successful save).</summary>
    public void AcceptCurrentAsBaseline()
    {
        OriginalSlot = ToCurrentSlot();
        OnDependentsChanged();
    }

    /// <summary>Roll mutable fields back to the loaded baseline.</summary>
    public void Revert()
    {
        ItemId = OriginalSlot.ItemId;
        Count = OriginalSlot.Count;
        Durability = OriginalSlot.Durability;
        MaxDurability = OriginalSlot.MaxDurability;
        AmmoInMagazine = OriginalSlot.AmmoInMagazine;
        LiquidLevel = OriginalSlot.LiquidLevel;
        LiquidType = OriginalSlot.LiquidType;
        DynamicState = OriginalSlot.DynamicState;
        PlayerMadeString = OriginalSlot.PlayerMadeString;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        var old = field;
        field = value;
        // Guarded: the interpolated message would otherwise allocate on every slider
        // tick / keystroke even with diagnostics off.
        if (Core.Diagnostics.EditorLog.Enabled)
        {
            Core.Diagnostics.EditorLog.Info("Edit", $"{Kind} slot {Index}: {propertyName} '{old}' → '{value}'");
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        // Most edited properties affect derived booleans/text; refresh them all.
        OnDependentsChanged();
        return true;
    }

    private void OnDependentsChanged()
    {
        foreach (var p in new[]
        {
            nameof(IsEmpty),
            nameof(HasIcon),
            nameof(DisplayName),
            nameof(ShowRolePlaceholder),
            nameof(CountText),
            nameof(ShowCount),
            nameof(DurabilityPercent),
            nameof(ShowDurability),
            nameof(DurabilityText),
            nameof(IsDirty),
            nameof(ValidationWarning),
            nameof(HasValidationWarning),
            nameof(CanUpgrade),
            nameof(CanDowngrade),
            nameof(IsUpgradable),
            nameof(IsUpgraded),
            nameof(CanDismantle),
            nameof(HasLiquidCapacity),
            nameof(MaxLiquid),
            nameof(LiquidCapacityText),
            nameof(LiquidOptions),
            nameof(SelectedLiquid),
            nameof(UpgradeText),
            nameof(UpgradeRequirementText),
            nameof(HasUpgradeRequirements),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }
}

public enum InventoryKind { Equipment, Hotbar, Main }
