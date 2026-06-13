using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Host-side <see cref="ISaveOperationContext"/> handed to an operation for one run. The
/// host owns the loaded <see cref="SaveGame"/> and the change flag; the operation only
/// reads/mutates and signals intent via <see cref="MarkChanged"/>.
/// </summary>
internal sealed class SaveOperationContext : ISaveOperationContext
{
    public SaveOperationContext(
        string filePath,
        SaveKind kind,
        SaveGame save,
        IReadOnlyDictionary<string, string> parameters,
        bool isDryRun,
        IPluginLog log)
    {
        FilePath = filePath;
        Kind = kind;
        Save = save;
        Parameters = parameters;
        IsDryRun = isDryRun;
        Log = log;
    }

    public string FilePath { get; }

    public SaveKind Kind { get; }

    public SaveGame Save { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    public bool IsDryRun { get; }

    public IPluginLog Log { get; }

    public bool HasChanges { get; private set; }

    public void MarkChanged() => HasChanges = true;
}
