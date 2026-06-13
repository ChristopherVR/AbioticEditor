using System.ComponentModel;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Codex;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Reference card for one trader: wiki identity + portrait extracted from the paks +
/// stock from DT_NPC_TraderItems. Read-only lore/reference (trades are barter, in-game).
/// </summary>
public sealed class TraderCardViewModel : INotifyPropertyChanged
{
    private readonly TraderInfo _info;
    private readonly Func<string, bool> _worldHasFlag;
    private string? _portraitPath;
    private bool _portraitRequested;

    public TraderCardViewModel(TraderInfo info, Func<string, bool> worldHasFlag)
    {
        _info = info;
        _worldHasFlag = worldHasFlag;
        var lore = TraderLore.ById.TryGetValue(info.Id, out var e) ? e : null;
        Name = lore?.Name ?? info.Id;
        Where = lore?.Where ?? string.Empty;
        Blurb = lore?.Blurb ?? string.Empty;
        Unlock = lore?.Unlock ?? string.Empty;
    }

    /// <summary>How/when this trader actually becomes a trader (curated; see TraderLore).</summary>
    public string Unlock { get; }

    public bool HasUnlock => !string.IsNullOrEmpty(Unlock);

    // ---------- per-world availability + full stock detail ----------

    /// <summary>Is this trader available in THIS world (its gating flags are set)?</summary>
    public bool IsAvailableHere => _info.RequiredFlags.Count == 0 || _info.RequiredFlags.All(_worldHasFlag);

    public string AvailabilityText
    {
        get
        {
            // Most traders carry no world-flag gate in the game data - you unlock them by
            // meeting/helping them or by finishing the story - so claiming "available from the
            // start" was wrong (e.g. Jimmy is post-game). Show the curated unlock condition.
            if (_info.RequiredFlags.Count == 0)
            {
                return HasUnlock
                    ? Unlock
                    : "Unlocked by meeting them in the world (no save flag tracks this trader).";
            }
            return IsAvailableHere
                ? "AVAILABLE in this world - gating flag(s) are set."
                : $"NOT YET in this world - needs: {string.Join(", ", _info.RequiredFlags.Where(f => !_worldHasFlag(f)))}";
        }
    }

    // Cached: the platform re-reads ItemsSource on refresh, and a fresh list per get
    // would both reset checkbox state and re-trigger selection events (see the WinUI
    // Picker feedback-loop fix). Invalidated by RefreshAvailability().
    private IReadOnlyList<TraderOfferRowViewModel>? _offerRows;

    /// <summary>Every item this trader offers, with the per-world unlock state.
    /// Locked rows are selectable for a partial unlock.</summary>
    public IReadOnlyList<TraderOfferRowViewModel> OfferDetails
    {
        get
        {
            if (_offerRows is not null) return _offerRows;
            var catalog = GameDataServices.Catalog;
            _offerRows = _info.Sells.Select(o =>
            {
                var entry = catalog?.Find(o.ItemId);
                var name = entry?.DisplayName ?? o.ItemId;
                var text = o.Count > 1 ? $"{o.Count}× {name}" : name;
                var unlocked = o.RequiredFlag is null || _worldHasFlag(o.RequiredFlag);
                var status = o.RequiredFlag is null ? "always stocked"
                    : unlocked ? $"unlocked ({o.RequiredFlag} is set)"
                    : $"locked - needs {o.RequiredFlag}";
                // Reuse the palette item-VM so the offer row gets the real icon and the
                // same encyclopedia detail (stats, description, crafted-by, sold-by).
                var item = entry is null ? null : new PaletteItemViewModel(entry);
                item?.EnsureIcon();
                return new TraderOfferRowViewModel(
                    o.ItemId, text, status, unlocked, o.RequiredFlag, item, OnRowSelectionChanged, SelectOffer);
            }).ToList();
            return _offerRows;
        }
    }

    private void OnRowSelectionChanged()
    {
        foreach (var p in new[]
        {
            nameof(HasSelectedStock), nameof(SelectedUnlockFlags), nameof(UnlockSelectedText),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    /// <summary>Flags of the locked stock rows the user ticked.</summary>
    public IReadOnlyList<string> SelectedStockFlags => OfferDetails
        .Where(r => r.IsSelected && !r.Unlocked && r.RequiredFlag is not null)
        .Select(r => r.RequiredFlag!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// The flags a selection-based unlock writes: the ticked stock flags PLUS any
    /// missing trader-gating flags (without those, the trader never appears and the
    /// unlocked stock would be unreachable).
    /// </summary>
    public IReadOnlyList<string> SelectedUnlockFlags => _info.RequiredFlags
        .Where(f => !_worldHasFlag(f))
        .Concat(SelectedStockFlags)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public bool HasSelectedStock => SelectedStockFlags.Count > 0;

    public string UnlockSelectedText => $"UNLOCK {SelectedStockFlags.Count} SELECTED";

    // ---------- per-offer item inspection ----------

    private PaletteItemViewModel? _selectedOfferItem;

    /// <summary>The stock item whose encyclopedia detail is shown (tap an offer icon).</summary>
    public PaletteItemViewModel? SelectedOfferItem
    {
        get => _selectedOfferItem;
        private set
        {
            if (ReferenceEquals(_selectedOfferItem, value)) return;
            _selectedOfferItem = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOfferItem)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedOfferItem)));
        }
    }

    public bool HasSelectedOfferItem => _selectedOfferItem is not null;

    /// <summary>Shows the encyclopedia detail for a tapped stock row (no-op for rows whose
    /// item isn't in the catalog).</summary>
    public void SelectOffer(TraderOfferRowViewModel row)
    {
        if (row.Item is null) return;
        row.Item.EnsureIcon();
        // Toggle off when the same row is tapped again.
        SelectedOfferItem = ReferenceEquals(_selectedOfferItem, row.Item) ? null : row.Item;
    }

    public void ClearSelectedOffer() => SelectedOfferItem = null;

    /// <summary>What the trader accepts as payment (barter).</summary>
    public string AcceptsText
    {
        get
        {
            var catalog = GameDataServices.Catalog;
            var names = _info.Accepts
                .Select(o => o.Count > 1
                    ? $"{o.Count}× {catalog?.Find(o.ItemId)?.DisplayName ?? o.ItemId}"
                    : catalog?.Find(o.ItemId)?.DisplayName ?? o.ItemId)
                .Distinct()
                .ToList();
            return names.Count == 0 ? "(barter terms vary)" : string.Join(", ", names);
        }
    }

    public string Id => _info.Id;
    public string Name { get; }
    public string Where { get; }
    public string Blurb { get; }

    // ---------- spoiler concealment ----------

    /// <summary>Per-item reveal key.</summary>
    public string SpoilerKey => SpoilerService.Key(SpoilerService.Trader, _info.Id);

    /// <summary>A trader not yet available in this world is content the player hasn't reached.</summary>
    public bool IsConcealed => SpoilerService.ShouldConceal(SpoilerKey, !IsAvailableHere);

    public string ShownName => SpoilerService.Mask(Name, IsConcealed, "▓ CLASSIFIED TRADER");
    public string ShownWhere => SpoilerService.Mask(Where, IsConcealed, string.Empty);
    public string ShownBlurb => SpoilerService.Mask(Blurb, IsConcealed, string.Empty);
    public string ShownSellsText => IsConcealed ? string.Empty : SellsText;
    public string ShownAvailabilityText => IsConcealed ? SpoilerService.ClassifiedTitle : AvailabilityText;
    public string ShownTapHint => IsConcealed ? "Above clearance - tap to reveal" : "Tap to open in the side panel →";

    /// <summary>Portrait stays hidden while sealed (the face spoils the trader).</summary>
    public bool ShowPortrait => HasPortrait && !IsConcealed;

    private void NotifyConcealment()
    {
        foreach (var p in new[]
        {
            nameof(IsConcealed), nameof(ShownName), nameof(ShownWhere), nameof(ShownBlurb),
            nameof(ShownSellsText), nameof(ShownAvailabilityText), nameof(ShownTapHint), nameof(ShowPortrait),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    /// <summary>Prompts to override clearance; on confirm reveals this trader permanently.</summary>
    public async Task RevealAsync()
    {
        if (!IsConcealed) return;
        if (await SpoilerPrompt.RevealAsync("This trader", SpoilerKey)) NotifyConcealment();
    }

    /// <summary>Every flag gating this trader or any of its offers.</summary>
    public IReadOnlyList<string> AllGatingFlags => _info.RequiredFlags
        .Concat(_info.Sells.Where(o => o.RequiredFlag is not null).Select(o => o.RequiredFlag!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Gating flags not yet set in this world.</summary>
    public IReadOnlyList<string> MissingFlags => AllGatingFlags.Where(f => !_worldHasFlag(f)).ToList();

    public bool HasMissingFlags => MissingFlags.Count > 0;

    /// <summary>Re-evaluates availability after world flags changed (also drops any
    /// stock selection - freshly unlocked rows are no longer selectable).</summary>
    public void RefreshAvailability()
    {
        _offerRows = null;
        // The freshly rebuilt rows own new item-VM instances, so any open detail card
        // would point at a stale row - drop it.
        SelectedOfferItem = null;
        foreach (var p in new[]
        {
            nameof(IsAvailableHere), nameof(AvailabilityText), nameof(OfferDetails),
            nameof(MissingFlags), nameof(HasMissingFlags),
            nameof(HasSelectedStock), nameof(SelectedUnlockFlags), nameof(UnlockSelectedText),
            nameof(IsConcealed), nameof(ShownName), nameof(ShownWhere), nameof(ShownBlurb),
            nameof(ShownSellsText), nameof(ShownAvailabilityText), nameof(ShownTapHint), nameof(ShowPortrait),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    public string RequiredFlagsText => _info.RequiredFlags.Count > 0
        ? $"appears after: {string.Join(", ", _info.RequiredFlags)}"
        : string.Empty;

    public bool HasRequiredFlags => _info.RequiredFlags.Count > 0;

    public string SellsText
    {
        get
        {
            var catalog = GameDataServices.Catalog;
            var names = _info.Sells
                .Select(o => catalog?.Find(o.ItemId)?.DisplayName ?? o.ItemId)
                .Distinct()
                .Take(10)
                .ToList();
            if (names.Count == 0) return "(no stock table)";
            var more = _info.Sells.Select(o => o.ItemId).Distinct().Count() - names.Count;
            return string.Join(", ", names) + (more > 0 ? $" +{more} more" : "");
        }
    }

    public string? PortraitPath
    {
        get
        {
            EnsurePortrait();
            return _portraitPath;
        }
    }

    public bool HasPortrait => _portraitPath is not null;

    private void EnsurePortrait()
    {
        if (_portraitRequested || _info.ImageAssetPath is null) return;
        _portraitRequested = true;

        var provider = GameDataServices.Provider;
        if (provider is null) return;

        _ = Task.Run(() =>
        {
            try
            {
                var path = provider.ExtractTextureByGameRef(_info.ImageAssetPath);
                if (path is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _portraitPath = path;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PortraitPath)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPortrait)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPortrait)));
                    });
                }
            }
            catch
            {
                // Portraits are cosmetic.
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One trader offer with its per-world unlock status (XAML row). Locked rows expose a
/// checkbox so the user can pick exactly which parts of the stock to unlock.
/// </summary>
public sealed class TraderOfferRowViewModel : INotifyPropertyChanged
{
    private readonly Action _selectionChanged;
    private readonly Action<TraderOfferRowViewModel> _inspect;
    private bool _isSelected;

    public TraderOfferRowViewModel(
        string itemId, string text, string status, bool unlocked, string? requiredFlag,
        PaletteItemViewModel? item, Action selectionChanged, Action<TraderOfferRowViewModel> inspect)
    {
        ItemId = itemId;
        Text = text;
        Status = status;
        Unlocked = unlocked;
        RequiredFlag = requiredFlag;
        Item = item;
        _selectionChanged = selectionChanged;
        _inspect = inspect;
    }

    public string ItemId { get; }
    public string Text { get; }
    public string Status { get; }
    public bool Unlocked { get; }

    /// <summary>The catalog-backed item VM for this offer (icon + encyclopedia), or null
    /// when the stock id isn't in the item catalog.</summary>
    public PaletteItemViewModel? Item { get; }

    public bool HasItem => Item is not null;

    /// <summary>Surfaces this offer's item detail in the trader card.</summary>
    public void Inspect() => _inspect(this);

    /// <summary>The world flag gating this offer (null = always stocked).</summary>
    public string? RequiredFlag { get; }

    /// <summary>Only locked, flag-gated rows can be picked for unlocking.</summary>
    public bool CanSelect => !Unlocked && RequiredFlag is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            // Guarded write-back: the platform re-seeds checkbox state on refresh.
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _selectionChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
