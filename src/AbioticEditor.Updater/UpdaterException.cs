namespace AbioticEditor.Updater;

/// <summary>An update operation failed (network, GitHub API, download, or install).</summary>
public class UpdaterException : Exception
{
    public UpdaterException()
    {
    }

    public UpdaterException(string message)
        : base(message)
    {
    }

    public UpdaterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The updater was asked to run before its repository coordinates were filled in (the
/// placeholders in <see cref="UpdaterOptions"/> are still in place).
/// </summary>
public sealed class UpdaterConfigurationException : UpdaterException
{
    public UpdaterConfigurationException(string message)
        : base(message)
    {
    }
}
