using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>One editable appearance field (head, hair, shirt color...) with its options.</summary>
public sealed class CustomizationFieldViewModel : INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private CustomizationOption? _selected;

    public CustomizationFieldViewModel(CustomizationField field, Action onChanged)
    {
        Field = field;
        _onChanged = onChanged;

        var options = GameDataServices.CustomizationOptions.TryGetValue(field.TableName, out var list)
            ? list
            : Array.Empty<CustomizationOption>();
        // The save value must always be selectable even when assets are unavailable or
        // the row vanished from the table (patch drift).
        if (!options.Any(o => string.Equals(o.RowName, field.CurrentValue, StringComparison.OrdinalIgnoreCase)))
        {
            options = options.Prepend(new CustomizationOption(field.CurrentValue, field.CurrentValue)).ToList();
        }
        Options = new ObservableCollection<CustomizationOption>(options);
        _selected = Options.FirstOrDefault(o => string.Equals(o.RowName, field.CurrentValue, StringComparison.OrdinalIgnoreCase));
    }

    public CustomizationField Field { get; }
    public string Label => Field.Label;
    public ObservableCollection<CustomizationOption> Options { get; }

    public CustomizationOption? Selected
    {
        get => _selected;
        set
        {
            if (Equals(_selected, value)) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            UpdatePreview();
            _onChanged();
        }
    }

    public string CurrentRowName => _selected?.RowName ?? Field.CurrentValue;
    public bool IsDirty => !string.Equals(CurrentRowName, Field.CurrentValue, StringComparison.Ordinal);

    // ---------- preview (icon texture or color swatch) ----------

    private string? _previewIconPath;
    private string? _previewedAssetPath;

    /// <summary>Extracted PNG of the selected option's CustomizationIcons texture.</summary>
    public string? PreviewIconPath
    {
        get
        {
            if (_previewIconPath is null) UpdatePreview();
            return _previewIconPath;
        }
    }

    public bool HasPreviewIcon => _previewIconPath is not null;

    public Color SwatchColor
    {
        get
        {
            // FLinearColor.Hex may carry an alpha tail - keep just RRGGBB.
            var hex = _selected?.ColorHex?.TrimStart('#');
            if (string.IsNullOrEmpty(hex)) return Colors.Transparent;
            if (hex.Length > 6) hex = hex[..6];
            return Color.TryParse($"#{hex}", out var c) ? c : Colors.Transparent;
        }
    }

    // Some rows (shirt colors) carry BOTH an icon and a color - drawing both stacked
    // the swatch over the image as a floating square. Icon wins; swatch is the fallback.
    public bool HasSwatch => !string.IsNullOrEmpty(_selected?.ColorHex) && !HasPreviewIcon;

    private void UpdatePreview()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SwatchColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSwatch)));

        var asset = _selected?.IconAssetPath;
        if (asset == _previewedAssetPath) return;
        _previewedAssetPath = asset;

        if (asset is null || GameDataServices.Provider is not { } provider)
        {
            _previewIconPath = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewIconPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreviewIcon)));
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                // Customization icons are real color renders (opaque, black background)
                // - usable directly, unlike the alpha-mask item icons.
                var path = provider.ExtractTextureByGameRef(asset);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _previewIconPath = path;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewIconPath)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreviewIcon)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSwatch)));
                });
            }
            catch
            {
                // Previews are cosmetic.
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Editor over the per-account <c>ScientistCustomization_&lt;slot&gt;.sav</c> (head, hair,
/// clothing, colors). This file lives outside the world folder and saves independently
/// of the player save.
/// </summary>
public sealed class CustomizationViewModel : INotifyPropertyChanged
{
    private readonly ulong _steamId;
    private CustomizationSaveFile? _file;
    private string _status = string.Empty;

    public CustomizationViewModel(long steamId64)
    {
        _steamId = steamId64 > 0 ? (ulong)steamId64 : 0;
        Slots = _steamId > 0 ? CustomizationSaveFile.SlotsFor(_steamId) : Array.Empty<int>();
        SaveCommand = new RelayCommand(Save, () => _file is not null && Fields.Any(f => f.IsDirty));
        _selectedSlot = Slots.Count > 0 ? Slots[0] : 0;
        LoadSlot(_selectedSlot);
    }

    public IReadOnlyList<int> Slots { get; }
    public bool IsAvailable => _file is not null;
    public bool IsUnavailable => _file is null;
    public bool HasMultipleSlots => Slots.Count > 1;

    /// <summary>Name of the file being edited, e.g. <c>ScientistCustomization_1.sav</c> (empty when none loaded).</summary>
    public string FileName => _file is not null ? Path.GetFileName(_file.FilePath) : string.Empty;

    /// <summary>
    /// Caption shown when the appearance file IS available: explains what is being edited
    /// and where it lives.
    /// </summary>
    public string AvailableCaption => _file is null
        ? string.Empty
        : $"Appearance is stored in {Path.GetFileName(_file.FilePath)}, a per-character file the game writes under your Steam account. "
          + "Editing here changes that file directly and keeps a .bak backup. It applies to every world this account plays.";

    /// <summary>
    /// Friendly explanation shown when the appearance file is NOT available: why it is missing
    /// and what the user can do about it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public string UnavailableExplanation =>
        "Appearance editing is unavailable because no ScientistCustomization save was found for this Steam account on this machine. "
        + "This file is not shipped with the game or the editor: the game writes it the first time you customize your character in-game, "
        + "and it is per Steam account and per machine. To enable editing, launch Abiotic Factor, customize your character once, then reopen the editor.";

    /// <summary>Voice isn't stored anywhere - it follows the chosen head's gender.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound from XAML; bindings need an instance member.")]
    public string VoiceNote =>
        "VOICE is not stored in any save - the game derives it from the head's gender (Dr. H / Dr. R).";

    public ObservableCollection<CustomizationFieldViewModel> Fields { get; } = new();

    private int _selectedSlot;

    public int SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (_selectedSlot == value) return;
            _selectedSlot = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSlot)));
            LoadSlot(value);
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public ICommand SaveCommand { get; }

    private void LoadSlot(int slot)
    {
        Fields.Clear();
        _file = null;
        if (_steamId == 0 || slot <= 0)
        {
            Status = "No ScientistCustomization save found for this Steam account on this machine.";
            Notify();
            return;
        }

        try
        {
            _file = CustomizationSaveFile.LoadFor(_steamId, slot);
        }
        catch (Exception ex)
        {
            Status = $"Failed to read customization save: {ex.Message}";
            Notify();
            return;
        }

        if (_file is null)
        {
            Status = "No ScientistCustomization save found for this Steam account on this machine.";
            Notify();
            return;
        }

        foreach (var f in _file.Fields)
        {
            Fields.Add(new CustomizationFieldViewModel(f, OnFieldChanged));
        }
        Status = $"Editing {Path.GetFileName(_file.FilePath)} - applies to every world this account plays.";
        Notify();
    }

    private void OnFieldChanged()
    {
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Save()
    {
        if (_file is null) return;
        try
        {
            var values = Fields.ToDictionary(f => f.Field.PropertyName, f => f.CurrentRowName);
            _file.Save(values);
            Status = $"Appearance saved at {DateTime.Now:HH:mm:ss} (.bak created). Restart the game to see it.";
            // Reload so baselines reset and IsDirty clears.
            LoadSlot(_selectedSlot);
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUnavailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableCaption)));
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
