namespace AbioticEditor.Web.Services;

/// <summary>
/// Web stand-in for the native sidebar's <c>LineBreakMode.MiddleTruncation</c> labels.
/// MAUI truncates pixel-fitted at render time; CSS has no middle-truncation mode, so the
/// web sidebar applies a character-budget version sized for the file pane instead. Like the
/// native mode it keeps both the start and the end (the extension) of a long save name
/// visible, with an ellipsis in the middle; the full path stays in the row tooltip.
/// </summary>
public static class MiddleTruncation
{
    public const char Ellipsis = '…';

    /// <summary>
    /// Returns <paramref name="value"/> unchanged when it fits within
    /// <paramref name="maxLength"/> characters; otherwise keeps the head and tail around a
    /// single middle ellipsis so the result is exactly <paramref name="maxLength"/> long.
    /// </summary>
    public static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? string.Empty;
        if (maxLength <= 1) return Ellipsis.ToString();
        var remaining = maxLength - 1;
        var tail = remaining / 2;
        var head = remaining - tail;
        return string.Concat(value.AsSpan(0, head), Ellipsis.ToString(), value.AsSpan(value.Length - tail));
    }
}
