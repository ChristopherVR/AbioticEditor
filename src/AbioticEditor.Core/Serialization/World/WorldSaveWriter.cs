using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Applies container mutations to the underlying <see cref="SaveGame"/> tree of a
/// <see cref="WorldSaveData"/>. Anything not edited re-serializes byte-perfect because
/// we only patch existing property <c>Value</c> fields - never replace structure.
/// </summary>
public static partial class WorldSaveWriter
{
    /// <summary>
    /// Writes <paramref name="data"/>'s raw save to disk. The previous file content is
    /// preserved as <c>&lt;path&gt;.bak</c> so one bad write can't destroy a save.
    /// </summary>
    public static void WriteToFile(WorldSaveData data, string path)
    {
        Diagnostics.EditorLog.Info("WorldSave", $"Writing {path} (previous content kept as {Path.GetFileName(path)}.bak)");
        try
        {
            Saves.SaveBackup.WriteWithBackup(path, data.Raw.WriteTo);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("WorldSave", $"Failed to write {path}", ex);
            throw;
        }
    }

    // ---------- lookup builders ----------
}
