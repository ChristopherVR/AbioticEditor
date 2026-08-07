using AbioticEditor.Ui;

namespace AbioticEditor.Web.Services;

/// <summary>Creates and reveals the diagnostics folder without routing its path through page alerts.</summary>
public static class LogFolderOpener
{
    public static async Task OpenAsync(
        IExternalNavigationService externalNavigation,
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalNavigation);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        Directory.CreateDirectory(logDirectory);
        await externalNavigation.RevealPathAsync(logDirectory, cancellationToken);
    }
}
