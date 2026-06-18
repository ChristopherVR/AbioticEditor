using System.ComponentModel;
using System.Windows.Input;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>One editable limb-health row inside a <see cref="WorldPetViewModel"/>.</summary>
public sealed class PetLimbViewModel : INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private double _value;

    public PetLimbViewModel(string key, double value, Action onChanged)
    {
        Key = key;
        _value = value;
        _onChanged = onChanged;
    }

    /// <summary>The full enum key, e.g. <c>EBodyLimbs::Head</c>.</summary>
    public string Key { get; }

    /// <summary>Short display name, e.g. <c>Head</c>.</summary>
    public string Label
    {
        get
        {
            var idx = Key.LastIndexOf(':');
            return idx >= 0 ? Key[(idx + 1)..] : Key;
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            if (Math.Abs(_value - value) < 0.0001) return;
            // Guarded: a slider can write back on every tick, so only build the diff string
            // when diagnostics are on.
            if (Core.Diagnostics.EditorLog.Enabled)
            {
                Core.Diagnostics.EditorLog.Info("Pet", $"Pet limb {Label}: {_value:F0} -> {value:F0}");
            }
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            _onChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One tamed pet from the <c>PetNPC</c> map, with a Fish-style detail editor: name,
/// life/health, level (XP), and creature type (upgrade / downgrade). Edits stage until the
/// world editor's SAVE; deletion is staged via <see cref="IsDeleted"/>.
/// </summary>
public sealed class WorldPetViewModel : INotifyPropertyChanged
{
    private WorldPet _original;
    private readonly Action _onChanged;

    private string _name;
    private bool _isDead;
    private int _xp;
    private string? _npcClass;
    private bool _isDeleted;
    private string? _worldSaveFileName;

    // The limb keys the creature actually uses, captured once from the loaded save. A limb is
    // "used" if it had health when loaded; HEAL / DOWN / REVIVE operate over exactly this set so
    // the transitions stay reversible (DOWN sets every used limb to 0, but a later HEAL still
    // knows which limbs to raise even though they all read 0 now).
    private readonly IReadOnlyList<string> _usedLimbKeys;

    // The pet's strongest limb when loaded, used as the "full" target for HEAL / REVIVE. The save
    // never stores the blueprint maximum, so this loaded peak is our best proxy. Captured once so
    // it survives a DOWN (which would otherwise zero out every limb and lose the reference value).
    private readonly double _fullLimbValue;

    public WorldPetViewModel(WorldPet source, Action onChanged, IReadOnlyList<PetVariant> variants)
    {
        _original = source;
        _onChanged = onChanged;
        _name = source.CustomName ?? string.Empty;
        _isDead = source.IsDead;
        _xp = source.Xp;
        _npcClass = source.NpcClass;
        Limbs = source.LimbHealth.Select(kv => new PetLimbViewModel(kv.Key, kv.Value, OnLimbChanged)).ToList();

        // Capture the used-limb set and the "full" value from the loaded state, before any edit.
        // Fall back to every limb (and a sensible default) when the pet loaded fully downed so the
        // buttons still have something to act on.
        var used = PetHealth.TrackedLimbs(source);
        _usedLimbKeys = used.Count > 0 ? used : source.LimbHealth.Keys.ToList();
        var loadedMax = PetHealth.MaxLimb(source);
        _fullLimbValue = loadedMax > 0 ? loadedMax : 100.0;

        // The picker lists every editable variant; ensure the pet's own current class is
        // present (even an unknown / summon one) so the selection round-trips.
        var options = variants.Where(v => v.IsEditable).ToList();
        var currentShort = PetCatalog.ShortOf(_npcClass);
        if (!options.Any(v => string.Equals(v.ShortClass, currentShort, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrEmpty(_npcClass))
        {
            options.Insert(0, new PetVariant(_npcClass!, currentShort, PetCatalog.FriendlyName(_npcClass) ?? currentShort,
                PetCatalog.Categorize(_npcClass), PetCatalog.IsSummon(_npcClass), IsEditable: true));
        }
        VariantOptions = options;
        VariantNames = options.Select(v => v.FriendlyName).ToList();
        _selectedVariant = options.FirstOrDefault(v => string.Equals(v.ShortClass, currentShort, StringComparison.OrdinalIgnoreCase));

        HealCommand = new RelayCommand(Heal);
        DownCommand = new RelayCommand(Down);
        ReviveCommand = new RelayCommand(Revive);
        DeleteCommand = new RelayCommand(() =>
        {
            IsDeleted = !IsDeleted;
            Core.Diagnostics.EditorLog.Info("Pet", $"World pet {(IsDeleted ? "marked for deletion" : "deletion cleared")}: '{DisplayName}' (id {Id})");
        });
    }

    public string Id => _original.Id;

    public IReadOnlyList<PetLimbViewModel> Limbs { get; }
    public IReadOnlyList<PetVariant> VariantOptions { get; }

    /// <summary>Friendly variant names for the upgrade picker (Picker of strings).</summary>
    public IReadOnlyList<string> VariantNames { get; }

    public ICommand HealCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand ReviveCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>True for armor-set summons (Exor / Mystagogue): shown, but not editable.</summary>
    public bool IsSummon => PetCatalog.IsSummon(_npcClass);

    public bool IsEditable => !IsSummon;

    public string DisplayName => !string.IsNullOrWhiteSpace(_name)
        ? _name
        : PetCatalog.FriendlyName(_npcClass) ?? _original.ShortClass;

    public string FamilyName => PetCatalog.Categorize(_npcClass).ToString();

    public string FriendlyClass => PetCatalog.FriendlyName(_npcClass) ?? _original.ShortClass;

    /// <summary>A single-letter avatar token (family initial), shown until the image loads.</summary>
    public string Avatar => FamilyName.Length > 0 ? FamilyName[..1] : "?";

    // ----- appearance (in-pak compendium portrait, like the containment creatures) -----

    private string? _imagePath;
    private bool _imageRequested;

    /// <summary>Extracted bestiary portrait for this pet, or null while loading / when absent.</summary>
    public string? ImagePath
    {
        get => _imagePath;
        private set { if (_imagePath != value) { _imagePath = value; Notify(nameof(ImagePath), nameof(HasImage)); } }
    }

    public bool HasImage => _imagePath is not null;

    /// <summary>Loads the pet portrait once, off-thread. Called when the pet is selected.</summary>
    public void EnsureImage()
    {
        if (_imageRequested) return;
        _imageRequested = true;
        LoadImage();
    }

    private void ReloadImage()
    {
        _imageRequested = false;
        ImagePath = null;
        EnsureImage();
    }

    private void LoadImage()
    {
        var provider = Services.GameDataServices.Provider;
        if (provider is null) return;
        var refs = PetCatalog.CompendiumTextureRefs(_npcClass);
        _ = Task.Run(() =>
        {
            foreach (var r in refs)
            {
                try
                {
                    var path = provider.ExtractTextureByGameRef(r);
                    if (path is not null)
                    {
                        MainThread.BeginInvokeOnMainThread(() => ImagePath = path);
                        return;
                    }
                }
                catch
                {
                    // Portraits are cosmetic.
                }
            }
        });
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            var old = _name;
            _name = value;
            Core.Diagnostics.EditorLog.Info("Pet", $"World pet renamed: '{old}' -> '{value}' (id {Id})");
            Notify(nameof(Name), nameof(DisplayName), nameof(IsDirty));
            _onChanged();
        }
    }

    public bool IsDead
    {
        get => _isDead;
        set
        {
            if (_isDead == value) return;
            _isDead = value;
            Core.Diagnostics.EditorLog.Info("Pet", $"World pet IsDead: {value} ('{DisplayName}', id {Id})");
            Notify(nameof(IsDead), nameof(StatusText), nameof(IsDirty));
            _onChanged();
        }
    }

    public int Xp
    {
        get => _xp;
        set
        {
            var v = Math.Max(0, value);
            if (_xp == v) return;
            var old = _xp;
            _xp = v;
            Core.Diagnostics.EditorLog.Info("Pet", $"World pet XP: {old} -> {v} ('{DisplayName}', id {Id})");
            Notify(nameof(Xp), nameof(Level), nameof(IsDirty));
            _onChanged();
        }
    }

    /// <summary>Level 0-20, derived from / written back to <see cref="Xp"/>.</summary>
    public int Level
    {
        get => PetCatalog.LevelForXp(_xp);
        set
        {
            var lvl = Math.Clamp(value, 0, PetCatalog.MaxLevel);
            if (lvl == Level) return;
            Core.Diagnostics.EditorLog.Info("Pet", $"World pet level: {Level} -> {lvl} ('{DisplayName}', id {Id})");
            Xp = PetCatalog.XpForLevel(lvl);
            Notify(nameof(Level));
        }
    }

    private PetVariant? _selectedVariant;

    /// <summary>The chosen variant's friendly name, two-way bound to the upgrade picker.</summary>
    public string? SelectedVariantName
    {
        get => _selectedVariant?.FriendlyName;
        set
        {
            if (value is null) return;
            var match = VariantOptions.FirstOrDefault(v =>
                string.Equals(v.FriendlyName, value, StringComparison.Ordinal));
            if (match is null || ReferenceEquals(match, _selectedVariant)) return;
            var old = _selectedVariant?.FriendlyName ?? FriendlyClass;
            _selectedVariant = match;
            _npcClass = match.ClassPath;
            Core.Diagnostics.EditorLog.Info("Pet", $"World pet variant: '{old}' -> '{match.FriendlyName}' (id {Id})");
            Notify(nameof(SelectedVariantName), nameof(DisplayName), nameof(FamilyName),
                nameof(FriendlyClass), nameof(Avatar), nameof(IsDirty));
            ReloadImage(); // mutating the creature changes which portrait to show
            _onChanged();
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value) return;
            _isDeleted = value;
            Notify(nameof(IsDeleted), nameof(StatusText), nameof(IsDirty));
            _onChanged();
        }
    }

    public string StatusText
    {
        get
        {
            if (_isDeleted) return "WILL BE DELETED";
            return PetHealth.Status(ToCurrent()) switch
            {
                PetStatus.Dead => "DEAD",
                PetStatus.Downed => "DOWNED",
                PetStatus.Hurt => "HURT",
                _ => "HEALTHY",
            };
        }
    }

    public double TotalHealth => Limbs.Sum(l => l.Value);

    // ----- location -----
    //
    // A pet only carries world coordinates, not an area/level id, so the best area indication is
    // the world (region) save it lives in. The save's file name is not known to this VM at
    // construction; the host (WorldEditorViewModel) can set WorldSaveFileName after building the
    // pet list so AreaName can resolve via WorldAreaCatalog.FriendlyNameFromSaveFile. Until then,
    // the raw coordinates are always shown.

    public double X => _original.X;
    public double Y => _original.Y;
    public double Z => _original.Z;

    /// <summary>
    /// The world-save file name (or full path) this pet lives in, e.g.
    /// <c>WorldSave_Facility_Office1.sav</c>. Optional: set by the host so <see cref="AreaName"/>
    /// can name the region. Pets carry only coordinates, not an area id, so this is the only way
    /// to surface a friendly area.
    /// </summary>
    public string? WorldSaveFileName
    {
        get => _worldSaveFileName;
        set
        {
            if (_worldSaveFileName == value) return;
            _worldSaveFileName = value;
            Notify(nameof(WorldSaveFileName), nameof(AreaName), nameof(HasAreaName), nameof(LocationText));
        }
    }

    /// <summary>Friendly region/area name derived from <see cref="WorldSaveFileName"/>, or null.</summary>
    public string? AreaName => WorldAreaCatalog.FriendlyNameFromSaveFile(_worldSaveFileName);

    public bool HasAreaName => !string.IsNullOrEmpty(AreaName);

    /// <summary>Read-only location line: the friendly area (when known) and the raw coordinates.</summary>
    public string LocationText
    {
        get
        {
            var coords = $"X {_original.X:0}, Y {_original.Y:0}, Z {_original.Z:0}";
            var area = AreaName;
            return string.IsNullOrEmpty(area) ? coords : $"{area}  ({coords})";
        }
    }

    // HEAL / DOWN / REVIVE all act over the captured used-limb set (_usedLimbKeys), never over the
    // currently-tracked (non-zero) limbs, so a DOWN that zeroes every limb does not strand HEAL /
    // REVIVE with nothing to raise. Each helper sets limb values directly on the VMs and then
    // refreshes the derived status, so the sliders, totals and status label all update at once.

    private void Heal()
    {
        Core.Diagnostics.EditorLog.Info("Pet", $"World pet healed: '{DisplayName}' (id {Id})");
        SetUsedLimbs(_fullLimbValue);
        IsDead = false;
        AfterStateChange();
    }

    private void Down()
    {
        // Down = alive but every used limb at 0 (the game's "critically wounded" state).
        Core.Diagnostics.EditorLog.Info("Pet", $"World pet downed: '{DisplayName}' (id {Id})");
        SetUsedLimbs(0);
        IsDead = false;
        AfterStateChange();
    }

    private void Revive()
    {
        // Bring a dead / downed pet back as Hurt: clear the death flag and restore used limbs to a
        // fraction of full (mirrors the wiki's companion-revive behaviour; at least 1 HP each).
        Core.Diagnostics.EditorLog.Info("Pet", $"World pet revived: '{DisplayName}' (id {Id})");
        var target = _fullLimbValue > 0 ? Math.Max(1, _fullLimbValue * 0.25) : 1;
        SetUsedLimbs(target);
        IsDead = false;
        AfterStateChange();
    }

    /// <summary>Sets every used limb (see <see cref="_usedLimbKeys"/>) to <paramref name="value"/>.</summary>
    private void SetUsedLimbs(double value)
    {
        var keys = new HashSet<string>(_usedLimbKeys, StringComparer.Ordinal);
        foreach (var l in Limbs)
        {
            if (keys.Contains(l.Key)) l.Value = value;
        }
    }

    /// <summary>
    /// Forces the derived status / totals to refresh after a bulk limb change. The per-limb
    /// <see cref="PetLimbViewModel.Value"/> setter already raises its own change and calls
    /// <see cref="OnLimbChanged"/>, but a no-op set (e.g. a limb already at the target) raises
    /// nothing, so re-notify here to keep the status label honest after every button press.
    /// </summary>
    private void AfterStateChange()
    {
        Notify(nameof(StatusText), nameof(TotalHealth), nameof(IsDirty));
        _onChanged();
    }

    private void ApplyLimbValues(IReadOnlyDictionary<string, double> values)
    {
        foreach (var l in Limbs)
        {
            if (values.TryGetValue(l.Key, out var v)) l.Value = v;
        }
    }

    private void OnLimbChanged()
    {
        Notify(nameof(StatusText), nameof(TotalHealth), nameof(IsDirty));
        _onChanged();
    }

    public bool IsDirty =>
        _isDeleted
        || !string.Equals(_name, _original.CustomName ?? string.Empty, StringComparison.Ordinal)
        || _isDead != _original.IsDead
        || _xp != _original.Xp
        || !string.Equals(_npcClass, _original.NpcClass, StringComparison.Ordinal)
        || Limbs.Any(l => !_original.LimbHealth.TryGetValue(l.Key, out var v) || Math.Abs(v - l.Value) > 0.0001);

    public WorldPet ToCurrent() => _original with
    {
        CustomName = string.IsNullOrWhiteSpace(_name) ? null : _name,
        IsDead = _isDead,
        Xp = _xp,
        NpcClass = _npcClass,
        LimbHealth = Limbs.ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal),
    };

    public void AcceptBaseline()
    {
        _original = ToCurrent();
        Notify(nameof(IsDirty));
    }

    public void Revert()
    {
        IsDeleted = false;
        Name = _original.CustomName ?? string.Empty;
        IsDead = _original.IsDead;
        Xp = _original.Xp;
        ApplyLimbValues(_original.LimbHealth);
        var currentShort = PetCatalog.ShortOf(_original.NpcClass);
        _selectedVariant = VariantOptions.FirstOrDefault(v =>
            string.Equals(v.ShortClass, currentShort, StringComparison.OrdinalIgnoreCase));
        _npcClass = _original.NpcClass;
        Notify(nameof(SelectedVariantName), nameof(DisplayName), nameof(FamilyName),
            nameof(FriendlyClass), nameof(Avatar), nameof(StatusText));
        ReloadImage();
    }

    private void Notify(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
