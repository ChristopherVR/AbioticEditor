using UeSaveGame;
using UeSaveGame.PropertyTypes;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Player identity surgery. A player's steamid64 lives in TWO places: the file name
/// (<c>Player_&lt;steamid64&gt;.sav</c>) AND the top-level <c>SaveIdentifier</c>
/// StrProperty inside the save (see dotnet/docs/research-slot-types.md, Q3). Renaming
/// the file alone leaves the content claiming the old owner, so both are rewritten
/// together.
/// </summary>
public static class PlayerSaveIdentity
{
    static PlayerSaveIdentity()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Re-homes the player save at <paramref name="sourcePath"/> to <paramref name="newId"/>:
    /// backs up the original (<c>.bak</c>), rewrites the top-level <c>SaveIdentifier</c>
    /// StrProperty (exact-name match), writes the result to <c>Player_&lt;newId&gt;.sav</c>
    /// next to the source and deletes the original file. Returns the new file's path.
    /// </summary>
    /// <exception cref="IOException">A save for <paramref name="newId"/> already exists.</exception>
    public static string ChangeSteamId(string sourcePath, ulong newId)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;

        Saves.SaveBackup.CreateFor(sourcePath);

        SaveGame save;
        using (var fs = File.OpenRead(sourcePath))
        {
            save = SaveGame.LoadFrom(fs);
        }

        Diagnostics.EditorLog.Info("PlayerSave",
            $"Rewriting SaveIdentifier and re-homing {Path.GetFileName(sourcePath)} → Player_{newId}.sav");
        var newPath = WriteAs(save, dir, newId);
        File.Delete(sourcePath);
        return newPath;
    }

    /// <summary>
    /// Duplicates the player save at <paramref name="sourcePath"/> to a NEW
    /// <c>Player_&lt;newId&gt;.sav</c> in the same folder, rewriting the copy's
    /// <c>SaveIdentifier</c> to <paramref name="newId"/>. Unlike <see cref="ChangeSteamId"/>
    /// the original file is kept - used by "copy from an existing player". Returns the new
    /// file's path.
    /// </summary>
    /// <exception cref="IOException">A save for <paramref name="newId"/> already exists.</exception>
    public static string CloneToNewId(string sourcePath, ulong newId)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        SaveGame save;
        using (var fs = File.OpenRead(sourcePath))
        {
            save = SaveGame.LoadFrom(fs);
        }
        Diagnostics.EditorLog.Info("PlayerSave",
            $"Cloning {Path.GetFileName(sourcePath)} → Player_{newId}.sav (new SaveIdentifier {newId})");
        return WriteAs(save, dir, newId);
    }

    /// <summary>
    /// Writes <paramref name="save"/> to <c>Player_&lt;newId&gt;.sav</c> in
    /// <paramref name="destDir"/> after stamping its <c>SaveIdentifier</c> with
    /// <paramref name="newId"/>. Refuses to overwrite an existing player file. Returns the
    /// new file's path. Shared by re-homing, cloning and new-from-template creation.
    /// </summary>
    /// <exception cref="IOException">A save for <paramref name="newId"/> already exists.</exception>
    public static string WriteAs(SaveGame save, string destDir, ulong newId)
    {
        ArgumentNullException.ThrowIfNull(save);
        var newPath = Path.Combine(destDir, $"Player_{newId}.sav");
        if (File.Exists(newPath))
        {
            throw new IOException($"Player_{newId}.sav already exists in {destDir}.");
        }

        SetSaveIdentifier(save, newId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var fs = File.Create(newPath);
        save.WriteTo(fs);
        return newPath;
    }

    /// <summary>
    /// Reads the top-level <c>SaveIdentifier</c> StrProperty - the steamid64 the save's
    /// content claims as owner (the counterpart of the file-name id). Null when absent.
    /// </summary>
    public static string? GetSaveIdentifier(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return save.Properties?
            .FirstOrDefault(t => t.Name?.Value == "SaveIdentifier")?
            .Property?.Value?.ToString();
    }

    /// <summary>
    /// Public entry to overwrite the top-level <c>SaveIdentifier</c> StrProperty (creating
    /// it if the save lacks one). Used when fabricating templates / new saves.
    /// </summary>
    public static void StampIdentifier(SaveGame save, string value)
    {
        ArgumentNullException.ThrowIfNull(save);
        SetSaveIdentifier(save, value);
    }

    private static void SetSaveIdentifier(SaveGame save, string value)
    {
        var tags = save.Properties ?? throw new InvalidDataException("Save has no properties.");

        // Exact-name match: unlike the blueprint-compiled fields, SaveIdentifier carries
        // no hash suffix.
        var tag = tags.FirstOrDefault(t => t.Name?.Value == "SaveIdentifier");
        if (tag?.Property is not null)
        {
            tag.Property.Value = new FString(value);
            return;
        }

        // Defensive: a save without the property gets one appended (every observed real
        // save carries it, right before SaveVersion).
        var name = new FString("SaveIdentifier");
        var type = new FPropertyTypeName(name: new FString(nameof(StrProperty)));
        var property = FProperty.Create(name, type);
        property.Value = new FString(value);
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
    }
}
