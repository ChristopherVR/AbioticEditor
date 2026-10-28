using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The advice shown under "the save could not be written". It names the cause when the cause is
/// known, and never sends a player hunting for a file-permission problem that is not there.
/// </summary>
public static class SaveFailureGuidance
{
    public static string For(Exception exception, HostLanguageService languages)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(languages);
        if (exception is AggregateException { InnerExceptions.Count: 1 } single) exception = single.InnerException!;
        return exception switch
        {
            // Written for the player: why this save can be viewed but not saved.
            SaveWriteBlockedException blocked => blocked.Message,
            UnauthorizedAccessException => languages.Resource("Workspace_SaveFailedDenied"),
            IOException io when (io.HResult & 0xFFFF) is 112 or 39 => languages.Resource("Workspace_SaveFailedDiskFull"),
            IOException => languages.Resource("Workspace_SaveFailedInUse"),
            _ => languages.Resource("Workspace_SaveFailedGeneric"),
        };
    }
}
