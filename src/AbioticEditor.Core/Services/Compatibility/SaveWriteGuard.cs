using UeSaveGame;

namespace AbioticEditor.Core.Compatibility;

/// <summary>Raised when a write is refused because the save's layout is not one this editor understands.</summary>
public sealed class SaveWriteBlockedException : InvalidOperationException
{
    /// <summary>Creates the exception with the player-facing <paramref name="message"/>.</summary>
    public SaveWriteBlockedException(string message, CompatibilitySeverity severity) : base(message)
    {
        Severity = severity;
    }

    /// <summary>Why the write was refused.</summary>
    public CompatibilitySeverity Severity { get; }
}

/// <summary>
/// The last check before a save goes back to disk. Reading is always allowed; writing a save
/// from a newer or restructured game layout is refused, because the readers cannot see its real
/// contents and an edit would write fields the game ignores or, worse, mix two layouts in one
/// file. The same rule applies to the editor, the command line and the plugin write path.
/// </summary>
public static class SaveWriteGuard
{
    /// <summary>
    /// Set to <c>1</c> to write saves this editor does not recognize anyway. For advanced users
    /// who have checked the result; a backup is still kept by every write.
    /// </summary>
    public const string OverrideEnvironmentVariable = "ABIOTIC_ALLOW_UNSUPPORTED_SAVES";

    /// <summary>The reason a write to <paramref name="save"/> is refused, or null when it may proceed.</summary>
    public static string? BlockReason(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (OverrideEnabled()) return null;

        var report = CompatibilityAnalyzer.Analyze(save);
        return report.Severity is CompatibilitySeverity.LayoutChanged or CompatibilitySeverity.NewerVersion
            ? report.Warning
            : null;
    }

    /// <summary>Throws <see cref="SaveWriteBlockedException"/> when writing <paramref name="save"/> must be refused.</summary>
    public static void EnsureWritable(SaveGame save)
    {
        var reason = BlockReason(save);
        if (reason is null) return;
        var severity = CompatibilityAnalyzer.Analyze(save).Severity;
        throw new SaveWriteBlockedException(reason, severity);
    }

    private static bool OverrideEnabled()
        => string.Equals(Environment.GetEnvironmentVariable(OverrideEnvironmentVariable), "1", StringComparison.Ordinal);
}
