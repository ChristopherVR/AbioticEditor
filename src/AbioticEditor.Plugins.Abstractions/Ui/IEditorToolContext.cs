using UeSaveGame;

namespace AbioticEditor.Plugins.Ui;

/// <summary>
/// Context passed to <see cref="IEditorTool.CreateView"/>. Gives a UI tool the host
/// services plus a read-only window onto whatever save the user currently has open, so a
/// tool can show live information (a stat dashboard, a validator) without reaching into the
/// host's view-models. Kept deliberately small and MAUI-free.
/// </summary>
public interface IEditorToolContext
{
    /// <summary>Host services: logging, data directory, versions, host kind.</summary>
    IPluginHost Host { get; }

    /// <summary>
    /// The save the editor currently has open, or null if none. Read-only snapshot - a UI
    /// tool that wants to *edit* should register a <see cref="Saves.ISaveOperation"/> and let
    /// the host run it through the backup/write path rather than mutating this directly.
    /// </summary>
    SaveGame? ActiveSave { get; }

    /// <summary>The detected category of <see cref="ActiveSave"/>, or null when none is open.</summary>
    SaveKind? ActiveSaveKind { get; }

    /// <summary>Absolute path of the open save, or null when none is open.</summary>
    string? ActiveSavePath { get; }

    /// <summary>
    /// Raised when the open save changes (user switched files or closed the editor). A tool
    /// can subscribe to refresh its view. The host unsubscribes the tool automatically when
    /// its view closes.
    /// </summary>
    event EventHandler? ActiveSaveChanged;
}
