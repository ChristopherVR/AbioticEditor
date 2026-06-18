using System.ComponentModel;
using System.Windows.Input;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// One pet a player is carrying (a hotbar / Companion-slot / backpack item), with in-place
/// editing: name, variant, level (XP), and health. Edits stage until the player editor's SAVE;
/// deletion is staged via <see cref="IsDeleted"/>.
/// </summary>
public sealed class CarriedPetViewModel : INotifyPropertyChanged
{
    private CarriedPet _original;
    private readonly Action _onChanged;

    private string? _name;
    private int _xp;
    private double _health;
    private double _maxHealth;
    private string _itemRow;
    private bool _isDeleted;

    public CarriedPetViewModel(CarriedPet source, Action onChanged)
    {
        _original = source;
        _onChanged = onChanged;
        _name = source.Name;
        _xp = source.Xp;
        _health = source.Health;
        _maxHealth = source.MaxHealth;
        _itemRow = source.ItemRow;

        // Held forms only in the picker (weapon forms are a separate item the player crafts).
        VariantNames = PetItemCatalog.Items.Where(i => !i.IsWeaponForm).Select(i => i.Friendly).Distinct().ToList();

        HealCommand = new RelayCommand(() =>
        {
            Core.Diagnostics.EditorLog.Info("Pet", $"Carried pet healed: '{DisplayName}' ({SlotText})");
            Health = _maxHealth > 0 ? _maxHealth : PetItemCatalog.DefaultMaxHealth;
        });
        DeleteCommand = new RelayCommand(() => IsDeleted = !IsDeleted);
    }

    public PetSlotKind Slot => _original.Slot;
    public int Index => _original.Index;
    public bool IsCompanionSlot => _original.IsCompanionSlot;
    public string SlotText => IsCompanionSlot ? "Companion slot" : $"{Slot} slot {Index}";

    public IReadOnlyList<string> VariantNames { get; }
    public ICommand HealCommand { get; }
    public ICommand DeleteCommand { get; }

    public string DisplayName => !string.IsNullOrWhiteSpace(_name)
        ? _name!
        : PetItemCatalog.FriendlyName(_itemRow) ?? _itemRow;

    public string Variant => PetItemCatalog.FriendlyName(_itemRow) ?? _itemRow;

    public string? Name
    {
        get => _name;
        set { if (_name != value) { var old = _name; _name = value; Core.Diagnostics.EditorLog.Info("Pet", $"Carried pet renamed: '{old}' -> '{value}' ({SlotText})"); Notify(nameof(Name), nameof(DisplayName), nameof(IsDirty)); _onChanged(); } }
    }

    public string? SelectedVariantName
    {
        get => Variant;
        set
        {
            if (value is null) return;
            var row = PetItemCatalog.ItemRowFor(value);
            if (row is null || string.Equals(row, _itemRow, StringComparison.Ordinal)) return;
            var old = Variant;
            _itemRow = row;
            Core.Diagnostics.EditorLog.Info("Pet", $"Carried pet variant: '{old}' -> '{value}' ({SlotText})");
            Notify(nameof(SelectedVariantName), nameof(Variant), nameof(DisplayName), nameof(IsDirty));
            _onChanged();
        }
    }

    public int Xp
    {
        get => _xp;
        set { var v = Math.Max(0, value); if (_xp != v) { _xp = v; Notify(nameof(Xp), nameof(Level), nameof(IsDirty)); _onChanged(); } }
    }

    public int Level
    {
        get => PetCatalog.LevelForXp(_xp);
        set { var lvl = Math.Clamp(value, 0, PetCatalog.MaxLevel); if (lvl != Level) { var old = Level; Core.Diagnostics.EditorLog.Info("Pet", $"Carried pet level: {old} -> {lvl} ('{DisplayName}')"); Xp = PetCatalog.XpForLevel(lvl); Notify(nameof(Level)); } }
    }

    public double Health
    {
        get => _health;
        set { if (Math.Abs(_health - value) > 0.0001) { _health = value; Notify(nameof(Health), nameof(IsDirty)); _onChanged(); } }
    }

    public double MaxHealth
    {
        get => _maxHealth;
        set { if (Math.Abs(_maxHealth - value) > 0.0001) { _maxHealth = value; Notify(nameof(MaxHealth), nameof(IsDirty)); _onChanged(); } }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set { if (_isDeleted != value) { _isDeleted = value; Notify(nameof(IsDeleted), nameof(StatusText), nameof(IsDirty)); _onChanged(); } }
    }

    public string StatusText => _isDeleted ? "WILL BE REMOVED" : $"Lv {Level}  hp {_health:0}/{_maxHealth:0}";

    public bool IsDirty =>
        _isDeleted
        || !string.Equals(_name, _original.Name, StringComparison.Ordinal)
        || _xp != _original.Xp
        || !string.Equals(_itemRow, _original.ItemRow, StringComparison.Ordinal)
        || Math.Abs(_health - _original.Health) > 0.0001
        || Math.Abs(_maxHealth - _original.MaxHealth) > 0.0001;

    public CarriedPet ToCurrent() => _original with
    {
        Name = string.IsNullOrWhiteSpace(_name) ? null : _name,
        Xp = _xp,
        Health = _health,
        MaxHealth = _maxHealth,
        ItemRow = _itemRow,
    };

    public void AcceptBaseline()
    {
        _original = ToCurrent();
        Notify(nameof(IsDirty));
    }

    public void Revert()
    {
        IsDeleted = false;
        Name = _original.Name;
        Xp = _original.Xp;
        Health = _original.Health;
        MaxHealth = _original.MaxHealth;
        _itemRow = _original.ItemRow;
        Notify(nameof(SelectedVariantName), nameof(Variant), nameof(DisplayName), nameof(StatusText));
    }

    private void Notify(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
