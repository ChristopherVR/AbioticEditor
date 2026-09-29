using System.Text.Json;

namespace AbioticEditor.Web.Services;

/// <summary>Advanced/expert host preferences, persisted the same way as <see cref="HostSpoilerPreferences"/>.</summary>
public sealed class HostAdvancedPreferences
{
    private readonly string _path;
    private bool _skipEquipSlotValidation;
    private bool _enable3DBaseView;

    public HostAdvancedPreferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "webadvanced.json")) { }
    public HostAdvancedPreferences(string path)
    {
        _path = path;
        var stored = Read(path);
        _skipEquipSlotValidation = stored.SkipEquipSlotValidation;
        _enable3DBaseView = stored.Enable3DBaseView;
    }

    /// <summary>
    /// When enabled, the equipment/transmog EquipSlot fit check (the game's own "this item
    /// cannot go in that slot" rule, see EquipSlotTypes/SlotDropRules) is skipped, so any item
    /// can be placed in any equipment, transmog or hotbar slot. Off by default: this lets the
    /// editor create combinations the game itself never allows a player to reach.
    /// </summary>
    public bool SkipEquipSlotValidation
    {
        get => _skipEquipSlotValidation;
        set { if (_skipEquipSlotValidation != value) { _skipEquipSlotValidation = value; Save(); } }
    }

    /// <summary>
    /// Shows the experimental 3D base view (the "3D VIEW" world tab and the button that opens it).
    /// Off by default: the 3D view is unfinished, so it stays hidden until the player opts in.
    /// </summary>
    public bool Enable3DBaseView
    {
        get => _enable3DBaseView;
        set { if (_enable3DBaseView != value) { _enable3DBaseView = value; Save(); } }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new Stored(_skipEquipSlotValidation, _enable3DBaseView)));
    }

    private static Stored Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) ?? new(false, false) : new(false, false); }
        catch (IOException) { return new(false, false); }
        catch (UnauthorizedAccessException) { return new(false, false); }
        catch (JsonException) { return new(false, false); }
    }

    private sealed record Stored(bool SkipEquipSlotValidation, bool Enable3DBaseView);
}
