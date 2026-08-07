using AbioticEditor.Ui;

namespace AbioticEditor.Web.Services;

/// <summary>Opens the diagnostics folder in the OS file manager, as the editor always has.</summary>
public sealed class DesktopLogDelivery(IExternalNavigationService externalNavigation) : IDiagnosticsLogDelivery
{
    public bool RevealsFolder => true;

    public Task DeliverAsync(string logDirectory, string currentLogPath, CancellationToken cancellationToken = default)
        => LogFolderOpener.OpenAsync(externalNavigation, logDirectory, cancellationToken);
}
