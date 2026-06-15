using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// A world-map feature surfaced as its own world-editor tab (power sockets, resource nodes,
/// NPC spawns, triggers, elevators, buttons, portals, trams, ...). One tab drives a single
/// <see cref="IWorldMapFeature"/> through a Fish-style master-detail UI: an entry list on the
/// left, the selected entry's typed fields on the right. Edits patch the live save tree
/// (<paramref name="raw"/>) immediately through the feature, mirroring every other world tab
/// (the change persists when the world editor's SAVE writes the tree, keeping a .bak); REVERT
/// restores each field to its loaded value.
/// </summary>
public sealed class WorldFeatureTabViewModel : INotifyPropertyChanged
{
    private readonly IWorldMapFeature _feature;
    private readonly SaveGame _raw;
    private readonly Action _onChanged;
    private readonly string? _saveFileName;
    private readonly Action<string>? _onOpenLink;
    private WorldFeatureEntryViewModel? _selectedEntry;
    private bool _isActive;
    private string? _wikiImagePath;
    private bool _isWikiImageLoading;
    private bool _wikiImageRequested;

    public WorldFeatureTabViewModel(
        IWorldMapFeature feature, SaveGame raw, Action onChanged, Action<WorldFeatureTabViewModel> onSelect,
        string? saveFileName = null, Action<string>? onOpenLink = null)
    {
        _feature = feature;
        _raw = raw;
        _onChanged = onChanged;
        _saveFileName = saveFileName;
        _onOpenLink = onOpenLink;
        Entries = new ObservableCollection<WorldFeatureEntryViewModel>(
            feature.Read(raw).Select(e => new WorldFeatureEntryViewModel(this, e)));
        SelectCommand = new RelayCommand(() => onSelect(this));
    }

    /// <summary>
    /// The world-save file this tab's entries came from, used to infer an entry's area when its
    /// key carries no level token (e.g. a GUID-keyed entry). Null when the host didn't supply it.
    /// </summary>
    internal string? SaveFileName => _saveFileName;

    /// <summary>Opens the entity an entry links to (e.g. the container a socket powers); null when unsupported.</summary>
    internal Action<string>? OnOpenLink => _onOpenLink;

    /// <summary>Stable lowercase token (also the CLI subcommand), e.g. <c>power-sockets</c>.</summary>
    public string Id => _feature.Id;

    /// <summary>Human title shown in the detail header, e.g. <c>Power Sockets</c>.</summary>
    public string DisplayName => _feature.DisplayName;

    /// <summary>Upper-cased title for the tab button, matching the other world tabs.</summary>
    public string Title => _feature.DisplayName.ToUpperInvariant();

    /// <summary>One-line explanation shown above the master-detail panes.</summary>
    public string Description => _feature.Description;

    public ObservableCollection<WorldFeatureEntryViewModel> Entries { get; }

    public int Count => Entries.Count;

    /// <summary>Command bound by the dynamic tab button; activates this tab on the owner.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>True when this feature's entries can be removed (drives the REMOVE button).</summary>
    public bool SupportsRemoval => _feature.SupportsRemoval;

    /// <summary>
    /// Upper-cased label for the remove/reset button (e.g. <c>REMOVE THIS ENTRY</c>, or
    /// <c>DISCONNECT</c> for power sockets), from the feature's <see cref="IWorldMapFeature.RemoveActionLabel"/>.
    /// </summary>
    public string RemoveActionText => _feature.RemoveActionLabel.ToUpperInvariant();

    /// <summary>True when this is the visible world tab (drives the button highlight).</summary>
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public WorldFeatureEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (Set(ref _selectedEntry, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedEntry)));
                // First selection lazily fetches the one representative wiki image for this type.
                if (value is not null) RequestWikiImage();
            }
        }
    }

    public bool HasSelectedEntry => _selectedEntry is not null;

    // ---------- per-type wiki image (one picture for the whole feature) ----------

    /// <summary>Local path to the cached wiki image for this feature type, or null.</summary>
    public string? WikiImagePath
    {
        get => _wikiImagePath;
        private set
        {
            if (Set(ref _wikiImagePath, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWikiImage)));
            }
        }
    }

    public bool HasWikiImage => _wikiImagePath is not null;

    public bool IsWikiImageLoading
    {
        get => _isWikiImageLoading;
        private set => Set(ref _isWikiImageLoading, value);
    }

    /// <summary>Required credit line shown under any displayed wiki image.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML, which requires an instance property.")]
    public string WikiAttribution => WikiImageCache.AttributionText;

    private void RequestWikiImage()
    {
        if (_wikiImageRequested) return;
        _wikiImageRequested = true;
        var candidates = FeatureWikiImageCatalog.CandidatesFor(_feature.Id);
        // Structures the wiki doesn't picture (power sockets, elevators, triggers, ...) simply
        // show no image.
        if (candidates.Count == 0) return;
        IsWikiImageLoading = true;
        _ = LoadWikiImageAsync(candidates);
    }

    private async Task LoadWikiImageAsync(IReadOnlyList<string> candidates)
    {
        string? path = null;
        try { path = await WikiImageCache.Default.GetFirstAsync(candidates).ConfigureAwait(false); }
        catch { /* offline / not found - just show no image */ }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            WikiImagePath = path;
            IsWikiImageLoading = false;
        });
    }

    // ---------- dirty / save / revert ----------

    public bool IsDirty => Entries.Any(e => e.IsDirty || e.IsDeleted);

    internal WorldEditResult ApplyField(string entryKey, string fieldId, string? value)
        => _feature.SetField(_raw, entryKey, fieldId, value);

    internal void OnEntryChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        _onChanged();
    }

    /// <summary>Writes staged removals into the raw tree (called during SAVE, before the writer runs).</summary>
    public void ApplyPendingDeletions()
    {
        foreach (var entry in Entries.Where(e => e.IsDeleted).ToList())
        {
            _feature.Remove(_raw, entry.Key);
        }
    }

    /// <summary>After a successful SAVE: drop removed rows and adopt current values as the baseline.</summary>
    public void AcceptBaseline()
    {
        foreach (var entry in Entries.Where(e => e.IsDeleted).ToList())
        {
            if (ReferenceEquals(_selectedEntry, entry)) SelectedEntry = null;
            Entries.Remove(entry);
        }
        foreach (var entry in Entries)
        {
            entry.AcceptBaseline();
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }

    /// <summary>Restores every field to its loaded value and un-stages any removals.</summary>
    public void Revert()
    {
        foreach (var entry in Entries)
        {
            entry.Revert();
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
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

/// <summary>One actor row of a <see cref="WorldFeatureTabViewModel"/> with its typed fields.</summary>
public sealed class WorldFeatureEntryViewModel : INotifyPropertyChanged
{
    private readonly WorldFeatureTabViewModel _tab;
    private bool _isDeleted;

    public WorldFeatureEntryViewModel(WorldFeatureTabViewModel tab, WorldMapEntry entry)
    {
        _tab = tab;
        Key = entry.Key;
        Label = entry.Label;
        LinkTargetId = entry.LinkTargetId;
        LinkLabel = entry.LinkLabel;
        Fields = new ObservableCollection<WorldFeatureFieldViewModel>(
            entry.Fields.Select(f => new WorldFeatureFieldViewModel(this, f)));
        RemoveCommand = new RelayCommand(() => IsDeleted = true, () => _tab.SupportsRemoval && !_isDeleted);
        RestoreCommand = new RelayCommand(() => IsDeleted = false, () => _isDeleted);
        OpenLinkCommand = new RelayCommand(
            () => { if (LinkTargetId is { } id) _tab.OnOpenLink?.Invoke(id); },
            () => HasLink);
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>Id of a linked entity this entry can jump to (e.g. the container a socket powers), or null.</summary>
    public string? LinkTargetId { get; }

    /// <summary>Button label for the link action (e.g. "Open Crafting Bench"), or null.</summary>
    public string? LinkLabel { get; }

    /// <summary>True when this entry links to something openable AND the host supports navigation.</summary>
    public bool HasLink => LinkTargetId is not null && _tab.OnOpenLink is not null;

    /// <summary>Navigates to the linked entity (the CONTAINERS tab selects the device's container).</summary>
    public ICommand OpenLinkCommand { get; }

    /// <summary>
    /// Friendly area/world this entry lives in (e.g. "Manufacturing West"), inferred from the
    /// entry key's level token, falling back to the tab's world-save file name. Empty when neither
    /// yields a recognisable area.
    /// </summary>
    public string Area => WorldAreaCatalog.FriendlyNameFor(Key, _tab.SaveFileName) ?? string.Empty;

    /// <summary>True when an <see cref="Area"/> could be inferred (drives the AREA row's visibility).</summary>
    public bool HasArea => Area.Length > 0;

    public ObservableCollection<WorldFeatureFieldViewModel> Fields { get; }

    /// <summary>True when this feature's entries can be removed (drives the REMOVE button).</summary>
    public bool SupportsRemoval => _tab.SupportsRemoval;

    /// <summary>Upper-cased remove-button label for this feature (e.g. <c>DISCONNECT</c>).</summary>
    public string RemoveActionText => _tab.RemoveActionText;

    /// <summary>Staged for removal on the next SAVE (kept in the list, shown struck-through).</summary>
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value) return;
            _isDeleted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeleted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotDeleted)));
            (RemoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RestoreCommand as RelayCommand)?.RaiseCanExecuteChanged();
            _tab.OnEntryChanged();
        }
    }

    public bool IsNotDeleted => !_isDeleted;

    /// <summary>Stages this entry for removal on the next SAVE.</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Un-stages a pending removal.</summary>
    public ICommand RestoreCommand { get; }

    /// <summary>Compact editable-field summary shown under the entry name in the list.</summary>
    public string Summary => string.Join("   ",
        Fields.Where(f => f.Editable).Select(f => $"{f.Id}={f.Value}"));

    public bool IsDirty => Fields.Any(f => f.IsDirty);

    internal WorldEditResult ApplyField(string fieldId, string? value)
        => _tab.ApplyField(Key, fieldId, value);

    internal void OnFieldChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        _tab.OnEntryChanged();
    }

    public void AcceptBaseline()
    {
        foreach (var field in Fields)
        {
            field.AcceptBaseline();
        }
    }

    public void Revert()
    {
        IsDeleted = false;
        foreach (var field in Fields)
        {
            field.RevertToBaseline();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One typed, possibly-editable field of an entry. Bools and choices apply on change; free-text
/// and numeric fields apply on <see cref="Commit"/> (wired to the entry's unfocus/completed) so a
/// half-typed value never round-trips through the feature's validation.
/// </summary>
public sealed class WorldFeatureFieldViewModel : INotifyPropertyChanged
{
    private readonly WorldFeatureEntryViewModel _entry;
    private readonly WorldMapField _field;
    private string? _value;     // last value successfully applied to the raw tree
    private string? _baseline;  // value at load / last save (REVERT target)
    private string? _draft;     // pending text for numeric / free-text entries
    private string? _error;

    public WorldFeatureFieldViewModel(WorldFeatureEntryViewModel entry, WorldMapField field)
    {
        _entry = entry;
        _field = field;
        _value = field.Value;
        _baseline = field.Value;
        _draft = field.Value;
    }

    public string Id => _field.Id;

    public string Label => _field.Label;

    public string? Hint => _field.Hint;

    public bool Editable => _field.Editable;

    public IReadOnlyList<string>? Options => _field.Options;

    public bool IsReadOnly => !_field.Editable;

    public bool IsBool => _field.Editable && _field.Kind == WorldFieldKind.Bool;

    public bool IsChoice => _field.Editable && _field.Kind == WorldFieldKind.Enum && Options is { Count: > 0 };

    public bool IsTextLike => _field.Editable && !IsBool && !IsChoice;

    public bool IsNumeric => _field.Kind is WorldFieldKind.Integer or WorldFieldKind.Number;

    public bool IsDirty => !string.Equals(_value, _baseline, StringComparison.Ordinal);

    /// <summary>Last applied value (read-only display and list summary).</summary>
    public string? Value => _value;

    /// <summary>Validation message from the last rejected edit, or null.</summary>
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>Two-way bound by the Switch for boolean fields; applies immediately.</summary>
    public bool BoolValue
    {
        get => string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase);
        set => Apply(value ? "true" : "false");
    }

    /// <summary>Two-way bound by the Picker for choice fields; applies immediately.</summary>
    public string? SelectedOption
    {
        get => _value;
        set
        {
            if (value is not null)
            {
                Apply(value);
            }
        }
    }

    /// <summary>Two-way bound by the Entry for numeric/free-text fields; applied on <see cref="Commit"/>.</summary>
    public string? Draft
    {
        get => _draft;
        set => _draft = value;
    }

    /// <summary>Applies the pending <see cref="Draft"/> (called on entry unfocus/completed).</summary>
    public void Commit() => Apply(_draft);

    private void Apply(string? candidate)
    {
        if (string.Equals(candidate, _value, StringComparison.Ordinal))
        {
            return;
        }

        var result = _entry.ApplyField(_field.Id, candidate);
        if (result.IsError)
        {
            Error = result.Error;
            NotifyValueViews();   // bounce bound controls back to the last good value
            return;
        }

        Error = null;
        _value = candidate;
        _draft = candidate;
        NotifyValueViews();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        _entry.OnFieldChanged();
    }

    public void AcceptBaseline()
    {
        _baseline = _value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }

    public void RevertToBaseline()
    {
        if (!IsDirty)
        {
            return;
        }

        // Patch the raw tree back to the loaded value. The baseline came from the same feature,
        // so it always re-validates.
        _entry.ApplyField(_field.Id, _baseline);
        _value = _baseline;
        _draft = _baseline;
        Error = null;
        NotifyValueViews();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }

    private void NotifyValueViews()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Draft)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolValue)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOption)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(Error))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
        }
        return true;
    }
}
