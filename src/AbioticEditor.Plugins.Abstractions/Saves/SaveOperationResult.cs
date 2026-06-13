namespace AbioticEditor.Plugins.Saves;

/// <summary>
/// The outcome of a <see cref="ISaveOperation"/> run, returned to the host so it can report
/// to the user and pick an exit code. A result never decides persistence on its own - that
/// is driven by <see cref="ISaveOperationContext.MarkChanged"/> - it only describes what
/// happened.
/// </summary>
/// <param name="Success">False signals a handled failure (bad input, unsupported save shape).</param>
/// <param name="Message">One-line summary shown to the user.</param>
/// <param name="ChangeCount">How many discrete edits were made (0 = nothing to do).</param>
public sealed record SaveOperationResult(bool Success, string Message, int ChangeCount = 0)
{
    /// <summary>A successful run that changed <paramref name="changeCount"/> things.</summary>
    public static SaveOperationResult Ok(string message, int changeCount = 0)
        => new(true, message, changeCount);

    /// <summary>A successful run that found nothing to change.</summary>
    public static SaveOperationResult NoChange(string message)
        => new(true, message, 0);

    /// <summary>A handled failure (reported to the user; not a crash).</summary>
    public static SaveOperationResult Failed(string message)
        => new(false, message, 0);
}
