using System.Text.Json;

namespace AbioticEditor.Web.Services;

/// <summary>Advanced/expert host preferences, persisted the same way as <see cref="HostSpoilerPreferences"/>.</summary>
public sealed class HostAdvancedPreferences
{
    private readonly string _path;
    private bool _skipEquipSlotValidation;

    public HostAdvancedPreferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "webadvanced.json")) { }
    public HostAdvancedPreferences(string path)
    {
        _path = path;
        _skipEquipSlotValidation = Read(path).SkipEquipSlotValidation;
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

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new Stored(_skipEquipSlotValidation)));
    }

    private static Stored Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) ?? new(false) : new(false); }
        catch (IOException) { return new(false); }
        catch (UnauthorizedAccessException) { return new(false); }
        catch (JsonException) { return new(false); }
    }

    private sealed record Stored(bool SkipEquipSlotValidation);
}
