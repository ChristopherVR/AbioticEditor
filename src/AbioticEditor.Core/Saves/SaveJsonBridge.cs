using UeSaveGame;
using UeSaveGame.Json;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.Saves;

/// <summary>
/// Adapts <see cref="UeSaveGame.Json.SaveGameSerializer"/> for the editor's "raw JSON"
/// tabs. Lets the UI dump any loaded save to a JSON string and apply edits back to a
/// <c>.sav</c> file on disk.
/// </summary>
public static class SaveJsonBridge
{
    /// <summary>Serializes a <see cref="SaveGame"/>'s full contents to a JSON string.</summary>
    public static string ToJson(SaveGame save)
    {
        AbioticSaveClasses.EnsureLoaded();
        var serializer = new SaveGameSerializer();
        // ConvertToJson reads from a stream - round-trip the save to a buffer first.
        using var savBytes = new MemoryStream();
        save.WriteTo(savBytes);
        savBytes.Position = 0;

        using var jsonOut = new MemoryStream();
        serializer.ConvertToJson(savBytes, jsonOut);
        return System.Text.Encoding.UTF8.GetString(jsonOut.ToArray());
    }

    /// <summary>
    /// Parses <paramref name="json"/> via <see cref="SaveGameSerializer"/> and writes
    /// the resulting save bytes to <paramref name="path"/>. Throws if the JSON is
    /// malformed or the save can't be reconstructed.
    /// </summary>
    public static void ApplyJsonToFile(string json, string path)
    {
        AbioticSaveClasses.EnsureLoaded();
        var serializer = new SaveGameSerializer();
        using var jsonIn = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        using var savOut = new MemoryStream();
        serializer.ConvertFromJson(jsonIn, savOut);

        // Only touch disk once the JSON parsed cleanly so we don't corrupt the file on
        // partial output.
        File.WriteAllBytes(path, savOut.ToArray());
    }

    /// <summary>
    /// Streams a <see cref="SaveGame"/>'s JSON straight to <paramref name="jsonPath"/>.
    /// Used by the export workflow - world saves serialize to 100+ MB of JSON, far too
    /// large to round-trip through an in-app text editor.
    /// </summary>
    public static void ExportJsonToFile(SaveGame save, string jsonPath)
    {
        Diagnostics.EditorLog.Info("JsonBridge", $"Exporting JSON to {jsonPath}");
        try
        {
            AbioticSaveClasses.EnsureLoaded();
            var serializer = new SaveGameSerializer();
            using var savBytes = new MemoryStream();
            save.WriteTo(savBytes);
            savBytes.Position = 0;

            using var jsonOut = File.Create(jsonPath);
            serializer.ConvertToJson(savBytes, jsonOut);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("JsonBridge", $"JSON export to {jsonPath} failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Converts an edited JSON file back into save bytes and writes them over
    /// <paramref name="savPath"/> (with the standard <c>.bak</c> backup). Throws if the
    /// JSON is malformed - the save file is untouched in that case.
    /// </summary>
    public static void ImportJsonFromFile(string jsonPath, string savPath)
    {
        Diagnostics.EditorLog.Info("JsonBridge", $"Importing JSON from {jsonPath} into {savPath} (+ .bak backup)");
        try
        {
            AbioticSaveClasses.EnsureLoaded();
            var serializer = new SaveGameSerializer();
            using var jsonIn = File.OpenRead(jsonPath);
            using var savOut = new MemoryStream();
            serializer.ConvertFromJson(jsonIn, savOut);

            Saves.SaveBackup.WriteWithBackup(savPath, savOut.WriteTo);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("JsonBridge", $"JSON import from {jsonPath} failed (save untouched unless backup line was logged)", ex);
            throw;
        }
    }
}
