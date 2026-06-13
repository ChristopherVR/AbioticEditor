using System.ComponentModel;
using System.Runtime.CompilerServices;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Mutable view-model for a single world-save door, enriched with metadata from
/// <see cref="DoorClassCatalog"/>, <see cref="DoorIdParser"/>, and
/// <see cref="DoorStateNames"/>.
/// </summary>
public sealed class WorldDoorViewModel : INotifyPropertyChanged
{
    public WorldDoorViewModel(WorldDoor door)
    {
        OriginalDoor = door;
        Id = door.Id;
        Kind = door.Kind;
        _doorState = door.DoorState;
        _yaw = door.Yaw;
        _oneWayUnlocked = door.OneWayUnlocked;
        _isDoorOpen = door.IsDoorOpen;
        _noReset = door.NoReset;

        var (map, actor) = DoorIdParser.Parse(door.Id);
        MapName = map;
        ActorName = actor;
        ClassName = DoorIdParser.ClassNameFromActor(actor);
        ClassInfo = DoorClassCatalog.Lookup(ClassName);
        if (ClassInfo.LockKind == "Unknown")
        {
            Core.Diagnostics.EditorLog.UnknownData(
                "DoorClass", ClassName, "door class not in catalog - newer game version?");
        }
    }

    public WorldDoor OriginalDoor { get; private set; }
    public string Id { get; }
    public WorldDoorKind Kind { get; }
    public bool IsSimple => Kind == WorldDoorKind.Simple;
    public bool IsSecurity => Kind == WorldDoorKind.Security;

    public string MapName { get; }
    public string ActorName { get; }
    public string ClassName { get; }
    public DoorClassInfo ClassInfo { get; }
    public string FriendlyClass => ClassInfo.DisplayName;
    public string LockKind => ClassInfo.LockKind;
    public string? RequiredKey => ClassInfo.RequiredKeyId;
    public bool HasRequiredKey => !string.IsNullOrEmpty(RequiredKey);
    public string LockChip => LockKind switch
    {
        "Keycard" => "KEYCARD",
        "Key"     => "KEY",
        "Part"    => "PART",
        "Flag"    => "STORY FLAG",
        "None"    => "FREE",
        _         => "UNKNOWN",
    };

    private string? _doorState;
    private double? _yaw;
    private bool? _oneWayUnlocked;
    private bool? _isDoorOpen;
    private bool? _noReset;

    public string? DoorState
    {
        get => _doorState;
        set
        {
            if (Set(ref _doorState, value))
            {
                _stateLabelsCache = null;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FriendlyState)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllStateLabels)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FriendlyStateForPicker)));
            }
        }
    }
    public double? Yaw { get => _yaw; set => Set(ref _yaw, value); }
    public bool? OneWayUnlocked { get => _oneWayUnlocked; set => Set(ref _oneWayUnlocked, value); }
    public bool? IsDoorOpen { get => _isDoorOpen; set => Set(ref _isDoorOpen, value); }
    public bool? NoReset { get => _noReset; set => Set(ref _noReset, value); }

    /// <summary>Friendly state label like "Closed", "Open", "Locked".</summary>
    public string FriendlyState => DoorStateNames.Friendly(_doorState);

    /// <summary>
    /// Friendly labels for the door-state Picker. When the save carries a state this
    /// build doesn't know (a future E_DoorStates member), its label is appended so the
    /// Picker can display it instead of showing an empty selection - and selecting it
    /// back is a no-op rather than data loss.
    /// </summary>
    private IReadOnlyList<string>? _stateLabelsCache;

    public IReadOnlyList<string> AllStateLabels
    {
        get
        {
            // Cached: a fresh list per read makes the WinUI Picker re-seed its platform
            // items on every binding pass, which can spiral into a SelectionChanged
            // feedback loop on the UI thread.
            if (_stateLabelsCache is not null) return _stateLabelsCache;

            var known = DoorStateNames.AllFriendlyNames;
            var currentIdx = DoorStateNames.TryParseIndex(_doorState);
            var currentIsKnown = _doorState is null
                || (currentIdx is { } i && i >= 0 && i < DoorStateNames.KnownStateCount);
            if (currentIsKnown) return _stateLabelsCache = known;

            var withUnknown = new List<string>(known.Count + 1);
            withUnknown.AddRange(known);
            withUnknown.Add(FriendlyState); // e.g. "State 7" - keeps the raw value selectable
            return _stateLabelsCache = withUnknown;
        }
    }

    /// <summary>
    /// Two-way binding for the door-state Picker. Reads/writes the friendly label; the
    /// underlying <see cref="DoorState"/> is converted to the matching
    /// <c>E_DoorStates::NewEnumerator{n}</c> raw value. The appended unknown-state
    /// label maps back to the save's original raw value untouched.
    /// </summary>
    public string FriendlyStateForPicker
    {
        get => FriendlyState;
        set
        {
            if (value is null) return;
            var knownIdx = DoorStateNames.AllFriendlyNames
                .Select((s, i) => (s, i))
                .FirstOrDefault(t => string.Equals(t.s, value, StringComparison.OrdinalIgnoreCase));
            if (knownIdx.s is not null)
            {
                DoorState = $"E_DoorStates::NewEnumerator{knownIdx.i}";
            }
            // Otherwise: the unknown-state entry (or stale text) - keep the raw value as-is.
        }
    }

    public string DisplayHeader => string.IsNullOrEmpty(MapName)
        ? FriendlyClass
        : $"{FriendlyClass}";

    public string DisplaySubtitle =>
        string.IsNullOrEmpty(MapName) ? ActorName : $"{MapName} · {ActorName}";

    public string KindLabel => Kind switch
    {
        WorldDoorKind.Simple => "SIMPLE",
        WorldDoorKind.Security => "SECURITY",
        _ => "?",
    };

    /// <summary>The required key/keycard's catalog display name, when the lock has one.</summary>
    public string RequiredKeyName => RequiredKey is { } key
        ? Services.GameDataServices.Catalog?.Find(key)?.DisplayName ?? key
        : string.Empty;

    /// <summary>
    /// The most precise location the save offers: the cooked sub-level the door actor is
    /// baked into, plus its actor instance name. Door entries store STATE only - no
    /// world coordinates exist for them anywhere in the save.
    /// </summary>
    public string LocationText
    {
        get
        {
            var map = string.IsNullOrEmpty(MapName) ? "(persistent level)" : MapName.Replace('_', ' ');
            return $"Sub-level: {map}\nActor: {ActorName}";
        }
    }

    /// <summary>How this door type works in-game (reference prose from <see cref="DoorClassCatalog"/>).</summary>
    public string AboutText => DoorClassCatalog.LockExplanation(LockKind);

    // ---------- wiki door image ----------

    private string? _doorImagePath;
    private bool _doorImageRequested;

    /// <summary>
    /// Local cached path of this door type's wiki image (downloaded once via
    /// <see cref="Core.Assets.WikiImageCache"/>), or <c>null</c> while loading or when
    /// the wiki has no picture of this class - true for almost every door type (see
    /// <see cref="DoorWikiImageCatalog"/>), in which case the detail panel keeps the
    /// sector card art with a "no door image" caption.
    /// </summary>
    public string? DoorImagePath
    {
        get
        {
            EnsureDoorImage();
            return _doorImagePath;
        }
    }

    public bool HasDoorImage => _doorImagePath is not null;

    private void EnsureDoorImage()
    {
        if (_doorImageRequested) return;
        _doorImageRequested = true;
        if (DoorWikiImageCatalog.WikiFileFor(ClassName) is not { } wikiFile) return;
        _ = LoadDoorImageAsync(wikiFile);
    }

    private async Task LoadDoorImageAsync(string wikiFile)
    {
        string? path = null;
        try
        {
            path = await Core.Assets.WikiImageCache.Default.GetAsync(wikiFile).ConfigureAwait(false);
        }
        catch
        {
            // Cosmetic; the cache already Warn-logged the failure.
        }
        if (path is null) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _doorImagePath = path;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DoorImagePath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDoorImage)));
        });
    }

    /// <summary>The required key item's own in-game description, when the catalog has it.</summary>
    public string RequiredKeyDescription => RequiredKey is { } key
        ? Services.GameDataServices.Catalog?.Find(key)?.Description ?? string.Empty
        : string.Empty;

    public bool HasRequiredKeyDescription => RequiredKeyDescription.Length > 0;

    public bool IsDirty =>
        _doorState != OriginalDoor.DoorState ||
        _yaw != OriginalDoor.Yaw ||
        _oneWayUnlocked != OriginalDoor.OneWayUnlocked ||
        _isDoorOpen != OriginalDoor.IsDoorOpen ||
        _noReset != OriginalDoor.NoReset;

    public WorldDoor ToCurrentDoor() => OriginalDoor with
    {
        DoorState = _doorState,
        Yaw = _yaw,
        OneWayUnlocked = _oneWayUnlocked,
        IsDoorOpen = _isDoorOpen,
        NoReset = _noReset,
    };

    public void AcceptBaseline() => OriginalDoor = ToCurrentDoor();

    public void Revert()
    {
        DoorState = OriginalDoor.DoorState;
        Yaw = OriginalDoor.Yaw;
        OneWayUnlocked = OriginalDoor.OneWayUnlocked;
        IsDoorOpen = OriginalDoor.IsDoorOpen;
        NoReset = OriginalDoor.NoReset;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        return true;
    }
}

/// <summary>
/// The selected door pinned on the game's own drawn sector map (pamphlet texture).
/// Pin coordinates are in texture-pixel space (projected by SectorMapCalibration);
/// the drawable letterboxes the texture into the view and scales the pins with it.
/// </summary>
public sealed class SectorMapPinDrawable : IDrawable
{
    private readonly string _imagePath;
    private readonly float _imageWidth;
    private readonly float _imageHeight;
    private readonly IReadOnlyList<(float X, float Y)> _context;
    private readonly (float X, float Y) _selected;
    private Microsoft.Maui.Graphics.IImage? _image;
    private bool _imageLoadFailed;

    public SectorMapPinDrawable(
        string imagePath,
        float imageWidth,
        float imageHeight,
        IReadOnlyList<(float X, float Y)> context,
        (float X, float Y) selected)
    {
        _imagePath = imagePath;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        _context = context;
        _selected = selected;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#101810");
        canvas.FillRectangle(dirtyRect);

        if (_image is null && !_imageLoadFailed)
        {
            try
            {
                using var fs = File.OpenRead(_imagePath);
                _image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(fs);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _imageLoadFailed = true;
            }
        }
        if (_image is null) return;

        var scale = Math.Min(dirtyRect.Width / _imageWidth, dirtyRect.Height / _imageHeight);
        var drawW = _imageWidth * scale;
        var drawH = _imageHeight * scale;
        var left = dirtyRect.Left + (dirtyRect.Width - drawW) / 2;
        var top = dirtyRect.Top + (dirtyRect.Height - drawH) / 2;
        canvas.DrawImage(_image, left, top, drawW, drawH);

        float Px(float x) => left + x * scale;
        float Py(float y) => top + y * scale;

        canvas.FillColor = Color.FromArgb("#CC2563EB");
        foreach (var (x, y) in _context)
        {
            canvas.FillCircle(Px(x), Py(y), 2.5f);
        }

        var accent = Color.FromArgb("#FF2D2D");
        var cx = Px(_selected.X);
        var cy = Py(_selected.Y);
        canvas.StrokeColor = accent;
        canvas.StrokeSize = 2f;
        canvas.DrawCircle(cx, cy, 9);
        canvas.DrawLine(cx - 14, cy, cx - 5, cy);
        canvas.DrawLine(cx + 5, cy, cx + 14, cy);
        canvas.DrawLine(cx, cy - 14, cx, cy - 5);
        canvas.DrawLine(cx, cy + 5, cx, cy + 14);
        canvas.FillColor = accent;
        canvas.FillCircle(cx, cy, 3.5f);
    }
}

/// <summary>
/// Top-down plot of every door in the selected door's sub-level, positions read from
/// the cooked map. The selected door gets a crosshair ring; the rest are context dots,
/// so the panel shows WHERE in the level the door physically sits.
/// </summary>
public sealed class DoorMiniMapDrawable : IDrawable
{
    private readonly IReadOnlyList<(string Actor, DoorWorldLocation Loc)> _doors;
    private readonly string _selectedActor;

    public DoorMiniMapDrawable(
        IReadOnlyList<(string Actor, DoorWorldLocation Loc)> doors, string selectedActor)
    {
        _doors = doors;
        _selectedActor = selectedActor;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#101810");
        canvas.FillRectangle(dirtyRect);
        if (_doors.Count == 0) return;

        var minX = _doors.Min(d => d.Loc.X);
        var maxX = _doors.Max(d => d.Loc.X);
        var minY = _doors.Min(d => d.Loc.Y);
        var maxY = _doors.Max(d => d.Loc.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min((dirtyRect.Width - 28) / spanX, (dirtyRect.Height - 28) / spanY);

        float Px(double x) => (float)(14 + (x - minX) * scale);
        float Py(double y) => (float)(14 + (y - minY) * scale);

        canvas.StrokeColor = Color.FromArgb("#1C271C");
        canvas.StrokeSize = 1;
        for (var gx = 0f; gx < dirtyRect.Width; gx += 36) canvas.DrawLine(gx, 0, gx, dirtyRect.Height);
        for (var gy = 0f; gy < dirtyRect.Height; gy += 36) canvas.DrawLine(0, gy, dirtyRect.Width, gy);

        foreach (var (actor, loc) in _doors)
        {
            var x = Px(loc.X);
            var y = Py(loc.Y);
            if (string.Equals(actor, _selectedActor, StringComparison.OrdinalIgnoreCase))
            {
                var accent = Color.FromArgb("#F5C518");
                canvas.StrokeColor = accent;
                canvas.StrokeSize = 1.5f;
                canvas.DrawCircle(x, y, 8);
                canvas.DrawLine(x - 12, y, x - 5, y);
                canvas.DrawLine(x + 5, y, x + 12, y);
                canvas.DrawLine(x, y - 12, x, y - 5);
                canvas.DrawLine(x, y + 5, x, y + 12);
                canvas.FillColor = accent;
                canvas.FillCircle(x, y, 3);
            }
            else
            {
                canvas.FillColor = Color.FromArgb("#5E8CCB");
                canvas.FillCircle(x, y, 2.5f);
            }
        }
    }
}
