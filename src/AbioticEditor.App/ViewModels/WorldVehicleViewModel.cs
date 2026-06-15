using System.ComponentModel;
using System.Windows.Input;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// One vehicle from a region save's <c>VehicleMap</c>, with a Fish-style detail editor:
/// appearance (wiki image), location + region, drivable ("unlock") / wrecked toggles,
/// move, reset-to-spawn, and a jump to its on-board inventory in the CONTAINERS tab.
/// </summary>
public sealed class WorldVehicleViewModel : INotifyPropertyChanged
{
    private WorldVehicle _original;
    private readonly Action _onChanged;
    private readonly Action<string> _openInventory;

    private bool _driveable;
    private bool _destroyed;
    private double _x, _y, _z;
    private double _qx, _qy, _qz, _qw;

    private string? _wikiImagePath;
    private bool _wikiImageRequested;
    private bool _isWikiImageLoading;

    private ActorTransform? _spawn;
    private bool _spawnRequested;
    private bool _spawnResolved;

    public WorldVehicleViewModel(WorldVehicle source, Action onChanged, Action<string> openInventory)
    {
        _original = source;
        _onChanged = onChanged;
        _openInventory = openInventory;
        _driveable = source.Driveable;
        _destroyed = source.Destroyed;
        _x = source.X; _y = source.Y; _z = source.Z;
        _qx = source.QuatX; _qy = source.QuatY; _qz = source.QuatZ; _qw = source.QuatW;

        ResetToSpawnCommand = new RelayCommand(ResetToSpawn, () => CanResetToSpawn);
        OpenInventoryCommand = new RelayCommand(() => _openInventory(_original.Id), () => HasInventory);
    }

    public string Id => _original.Id;
    public string DisplayName => _original.DisplayName;
    public string ShortClass => _original.ShortClass;
    public string Region => _original.Region;

    /// <summary>
    /// Friendly in-game area name for this vehicle's region (e.g. "Manufacturing West"),
    /// or empty when the region token is unknown. Used for a read-only AREA line by the LOCATION editor.
    /// </summary>
    public string AreaName => WorldAreaCatalog.FriendlyName(_original.Region) ?? string.Empty;

    /// <summary>True when <see cref="AreaName"/> is non-empty, gating the AREA readout's visibility.</summary>
    public bool HasArea => AreaName.Length > 0;

    /// <summary>
    /// False for decorative / scripted vehicles (the SnowGlobe Sleigh, Tram, Minecart): they
    /// are listed and shown, but the detail offers no drivable / wrecked / move controls.
    /// </summary>
    public bool IsEditable => VehicleCatalog.IsEditable(_original.VehicleClass);

    /// <summary>Inverse of <see cref="IsEditable"/>, for "display only" UI affordances.</summary>
    public bool IsDisplayOnly => !IsEditable;

    public ICommand ResetToSpawnCommand { get; }
    public ICommand OpenInventoryCommand { get; }

    public bool Driveable
    {
        get => _driveable;
        set { if (_driveable != value) { _driveable = value; Notify(nameof(Driveable), nameof(StateText), nameof(IsDirty)); _onChanged(); } }
    }

    public bool Destroyed
    {
        get => _destroyed;
        set { if (_destroyed != value) { _destroyed = value; Notify(nameof(Destroyed), nameof(StateText), nameof(IsDirty)); _onChanged(); } }
    }

    public string StateText => _destroyed ? "WRECKED" : _driveable ? "DRIVABLE" : "LOCKED";

    public double X { get => _x; set { if (Math.Abs(_x - value) > 1e-6) { _x = value; OnMoved(); } } }
    public double Y { get => _y; set { if (Math.Abs(_y - value) > 1e-6) { _y = value; OnMoved(); } } }
    public double Z { get => _z; set { if (Math.Abs(_z - value) > 1e-6) { _z = value; OnMoved(); } } }

    private void OnMoved()
    {
        Notify(nameof(X), nameof(Y), nameof(Z), nameof(LocationText), nameof(AtSpawn), nameof(SpawnStatusText), nameof(IsDirty));
        _onChanged();
    }

    public string LocationText => $"X {_x:F0}   Y {_y:F0}   Z {_z:F0}";

    public bool HasInventory => _original.HasInventory;
    public string InventoryText => _original.HasInventory
        ? $"{_original.InventoryItemCount} item(s) on board"
        : "no on-board storage";

    // ----- appearance (abioticfactor.wiki.gg, same mechanism as the fish codex) -----

    public IReadOnlyList<string> WikiImageCandidates => VehicleCatalog.WikiImageCandidates(_original.VehicleClass);
    public bool HasWikiImage => _wikiImagePath is not null;
    public bool IsWikiImageLoading => _isWikiImageLoading;
    public string? WikiImagePath { get => _wikiImagePath; private set { _wikiImagePath = value; } }

    // ----- spawn / reset -----

    public bool CanResetToSpawn => _spawnResolved && _spawn is not null;
    public string SpawnStatusText
    {
        get
        {
            if (!_spawnResolved) return "Resolving spawn position…";
            if (_spawn is not { } s) return "Spawn position unavailable (game install / mappings needed).";
            return AtSpawn ? "At spawn position." : $"Moved from spawn ({s.X:F0}, {s.Y:F0}, {s.Z:F0}).";
        }
    }

    public bool AtSpawn
    {
        get
        {
            if (_spawn is not { } s) return false;
            var dx = _x - s.X; var dy = _y - s.Y; var dz = _z - s.Z;
            return (dx * dx) + (dy * dy) + (dz * dz) < 100.0 * 100.0;
        }
    }

    /// <summary>Called when this vehicle becomes the selected detail; kicks off async loads.</summary>
    public void OnSelected()
    {
        RequestWikiImage();
        RequestSpawn();
    }

    private void RequestWikiImage()
    {
        if (_wikiImageRequested || WikiImageCandidates.Count == 0) return;
        _wikiImageRequested = true;
        _isWikiImageLoading = true;
        MainThread.BeginInvokeOnMainThread(() => Notify(nameof(IsWikiImageLoading)));
        _ = LoadWikiImageAsync();
    }

    private async Task LoadWikiImageAsync()
    {
        string? path = null;
        try { path = await WikiImageCache.Default.GetFirstAsync(WikiImageCandidates).ConfigureAwait(false); }
        catch { /* offline / not found: stays null, block collapses */ }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _wikiImagePath = path;
            _isWikiImageLoading = false;
            Notify(nameof(IsWikiImageLoading), nameof(WikiImagePath), nameof(HasWikiImage));
        });
    }

    private void RequestSpawn()
    {
        if (_spawnRequested) return;
        _spawnRequested = true;
        _ = ResolveSpawnAsync();
    }

    private async Task ResolveSpawnAsync()
    {
        ActorTransform? spawn = null;
        try
        {
            spawn = await Task.Run(() => Services.GameDataServices.Provider?.TryGetActorTransform(
                _original.VehicleId ?? _original.Id)).ConfigureAwait(false);
        }
        catch { /* unresolved: reset stays disabled */ }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _spawn = spawn;
            _spawnResolved = true;
            Notify(nameof(CanResetToSpawn), nameof(SpawnStatusText), nameof(AtSpawn));
            (ResetToSpawnCommand as RelayCommand)?.RaiseCanExecuteChanged();
        });
    }

    private void ResetToSpawn()
    {
        if (_spawn is not { } s) return;
        _x = s.X; _y = s.Y; _z = s.Z;
        _qx = s.QuatX; _qy = s.QuatY; _qz = s.QuatZ; _qw = s.QuatW;
        Notify(nameof(X), nameof(Y), nameof(Z), nameof(LocationText), nameof(AtSpawn), nameof(SpawnStatusText), nameof(IsDirty));
        _onChanged();
    }

    public bool IsDirty =>
        _driveable != _original.Driveable
        || _destroyed != _original.Destroyed
        || Math.Abs(_x - _original.X) > 1e-6
        || Math.Abs(_y - _original.Y) > 1e-6
        || Math.Abs(_z - _original.Z) > 1e-6
        || Math.Abs(_qx - _original.QuatX) > 1e-9
        || Math.Abs(_qy - _original.QuatY) > 1e-9
        || Math.Abs(_qz - _original.QuatZ) > 1e-9
        || Math.Abs(_qw - _original.QuatW) > 1e-9;

    public WorldVehicle ToCurrent() => _original with
    {
        Driveable = _driveable,
        Destroyed = _destroyed,
        X = _x, Y = _y, Z = _z,
        QuatX = _qx, QuatY = _qy, QuatZ = _qz, QuatW = _qw,
    };

    public void AcceptBaseline()
    {
        _original = ToCurrent();
        Notify(nameof(IsDirty), nameof(AtSpawn), nameof(SpawnStatusText));
    }

    public void Revert()
    {
        _driveable = _original.Driveable;
        _destroyed = _original.Destroyed;
        _x = _original.X; _y = _original.Y; _z = _original.Z;
        _qx = _original.QuatX; _qy = _original.QuatY; _qz = _original.QuatZ; _qw = _original.QuatW;
        Notify(nameof(Driveable), nameof(Destroyed), nameof(StateText), nameof(X), nameof(Y), nameof(Z),
            nameof(LocationText), nameof(AtSpawn), nameof(SpawnStatusText), nameof(IsDirty));
    }

    private void Notify(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
