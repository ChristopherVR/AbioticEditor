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

    /// <summary>
    /// The part of a failure that is worth showing the player, or <paramref name="fallback"/> when
    /// there is none.
    /// </summary>
    /// <remarks>
    /// <para>Most exceptions are technical accidents whose text means nothing to a player, which is
    /// why screens otherwise show authored copy instead. But some are the editor refusing on
    /// purpose - "that folder already contains a save", "this world has several characters, so one
    /// account id will not do" - and those messages ARE the authored copy: they were written for
    /// the player and they say the one thing that makes the problem fixable. Flattening them into
    /// a generic "it failed" is how someone ends up unable to tell a refusal from a bug.</para>
    ///
    /// <para>Deliberate refusals are identified by type. Everything else - a null reference, an
    /// out-of-range index, a parse error deep in a save - falls back to authored copy, so no raw
    /// internals reach the screen. Screens call this rather than reading an exception themselves,
    /// which is what keeps that judgement in one place.</para>
    /// </remarks>
    public string Detail(Exception exception, string action, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Record(exception, action);
        return exception is InvalidOperationException or InvalidDataException or ArgumentException
            or IOException or UnauthorizedAccessException or NotSupportedException
            ? exception.Message
            : fallback;
    }

    /// <summary>
    /// True when opening a folder failed because this browser has no folder picker at all
    /// (Firefox, Safari), rather than because something was wrong with the folder. The two
    /// deserve completely different advice: one is "your browser cannot do this", the other is
    /// "check your folder", and telling a Firefox player the second sends them hunting for a
    /// fault that does not exist.
    /// </summary>
    /// <remarks>
    /// Matched on the message because the failure crosses the JavaScript boundary as a plain
    /// <c>JSException</c> with nothing else to switch on. It lives here rather than in the screen
    /// so no screen has to touch an exception's internals to decide what to say.
    /// </remarks>
    public static bool IsFolderPickerUnavailable(Exception? exception)
        => exception?.Message.Contains("cannot open a folder", StringComparison.OrdinalIgnoreCase) == true;
}
