namespace AbioticEditor.Plugins.Events;

/// <summary>
/// A notification the host raises and plugins can react to (the "trigger when X happens"
/// extension point). Carries a name and a small bag of named data; helpers pull out the
/// common fields without the handler having to cast.
///
/// <para>
/// Handlers are <see cref="Action{PluginEvent}"/> registered via
/// <see cref="IPluginRegistry.AddEventHandler"/>. They run synchronously on whatever thread
/// raised the event and must be quick and non-throwing - the host wraps each in a try/catch
/// and logs failures, but a slow handler stalls the action that fired it.
/// </para>
/// </summary>
public sealed class PluginEvent
{
    /// <summary>Creates an event with a name and optional named payload.</summary>
    public PluginEvent(string name, IReadOnlyDictionary<string, object?>? data = null)
    {
        Name = name;
        Data = data ?? new Dictionary<string, object?>();
    }

    /// <summary>The event name, e.g. <see cref="PluginEvents.SaveOpened"/>.</summary>
    public string Name { get; }

    /// <summary>Named payload values supplied by the host for this event.</summary>
    public IReadOnlyDictionary<string, object?> Data { get; }

    /// <summary>Reads a payload value, or null if absent.</summary>
    public object? Get(string key) => Data.TryGetValue(key, out var v) ? v : null;

    /// <summary>Reads a payload value as a string, or null.</summary>
    public string? GetString(string key) => Get(key) as string;

    /// <summary>Convenience for the most common payload: the save file path involved.</summary>
    public string? SavePath => GetString("savePath");

    /// <summary>Convenience: the save kind involved, when the host supplied it.</summary>
    public SaveKind? SaveKind => Get("saveKind") is SaveKind k ? k : null;
}

/// <summary>
/// Well-known event names the host raises. Plugins may also define and (in principle) raise
/// their own; these are the ones the editor itself emits.
/// </summary>
public static class PluginEvents
{
    /// <summary>Raised once after plugins finish loading at startup. No save payload.</summary>
    public const string AppStarted = "app.started";

    /// <summary>The user opened/selected a save in the editor. Payload: savePath, saveKind.</summary>
    public const string SaveOpened = "save.opened";

    /// <summary>The editor was closed/returned home with no save open. No payload.</summary>
    public const string SaveClosed = "save.closed";

    /// <summary>
    /// A save file was written to disk by a plugin save operation. Payload: savePath,
    /// saveKind, operationId. Fires after the backup + write, so the file on disk is current.
    /// </summary>
    public const string SaveWritten = "save.written";
}
