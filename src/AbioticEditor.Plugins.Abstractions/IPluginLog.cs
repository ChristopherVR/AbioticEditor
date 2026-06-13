namespace AbioticEditor.Plugins;

/// <summary>
/// Logging surface handed to a plugin. The host routes these to its own diagnostics
/// (the editor log), tagged with the plugin's id, so a misbehaving plugin is traceable
/// without it needing a reference to the host's logging internals.
/// </summary>
public interface IPluginLog
{
    /// <summary>Informational progress line.</summary>
    void Info(string message);

    /// <summary>A recoverable problem the user may want to know about.</summary>
    void Warn(string message);

    /// <summary>A failure; optionally carries the exception for the log file.</summary>
    void Error(string message, Exception? exception = null);
}
