namespace AbioticEditor.Web.Services;

/// <summary>
/// Records the technical cause of a failed UI action while returning only safe,
/// actionable copy for display to the player.
/// </summary>
public sealed class UserFacingErrorService(ILogger<UserFacingErrorService> logger)
{
    private static readonly Action<ILogger, string, Exception?> LogActionFailure = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(1001, "PlayerActionFailed"),
        "Player action failed: {Action}");

    public string Present(Exception exception, string action, string guidance)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(guidance);

        LogActionFailure(logger, action, exception);
        return $"{action} {guidance}";
    }

    public void Record(Exception exception, string action)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        LogActionFailure(logger, action, exception);
    }
}
