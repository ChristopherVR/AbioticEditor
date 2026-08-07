namespace AbioticEditor.Web.Services;

/// <summary>
/// Gets the diagnostics log into the player's hands so they can attach it to a bug report.
/// </summary>
/// <remarks>
/// The desktop host reveals the log folder in the OS file manager. A browser tab has no file
/// manager, and its log lives in the in-memory file system WebAssembly provides, so there is
/// nothing to reveal - it downloads the file instead. <see cref="RevealsFolder"/> lets the
/// settings screen label the button for what it will actually do.
/// </remarks>
public interface IDiagnosticsLogDelivery
{
    /// <summary>True when this host opens the log folder; false when it downloads the log file.</summary>
    bool RevealsFolder { get; }

    /// <summary>Reveals the folder, or delivers the current log file, depending on the host.</summary>
    Task DeliverAsync(string logDirectory, string currentLogPath, CancellationToken cancellationToken = default);
}
