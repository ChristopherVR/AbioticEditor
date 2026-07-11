namespace AbioticEditor.Core.Ini;

/// <summary>What a parsed line is. Only KeyValue and SectionHeader are interpreted.</summary>
public enum IniLineKind
{
    /// <summary>Whitespace-only line.</summary>
    Blank,
    /// <summary>Line whose first non-space char is <c>;</c> or <c>#</c>.</summary>
    Comment,
    /// <summary><c>[Name]</c> header.</summary>
    SectionHeader,
    /// <summary><c>Key=Value</c> (split on the FIRST <c>=</c>; values may contain more).</summary>
    KeyValue,
    /// <summary>Anything else - preserved verbatim, never touched by edits.</summary>
    Other,
}

/// <summary>
/// One physical line: raw text (no terminator) + its own terminator, with the parsed
/// interpretation derived once at construction. Reference identity matters - section
/// views track their header line by reference - so this is deliberately a class, and
/// edits replace the whole line (except the terminator, which may be granted to a
/// final line when content is appended after it).
/// </summary>
internal sealed class IniLine
{
    private IniLine(string text, string terminator, IniLineKind kind, string? key, string? value)
    {
        Text = text;
        Terminator = terminator;
        Kind = kind;
        Key = key;
        Value = value;
    }

    public string Text { get; }
    public string Terminator { get; set; }
    public IniLineKind Kind { get; }

    /// <summary>Trimmed key for KeyValue lines, section name for headers; null otherwise.</summary>
    public string? Key { get; }

    /// <summary>Everything after the first <c>=</c>, verbatim. Null for non-KeyValue lines.</summary>
    public string? Value { get; }

    public static IniLine FromRaw(string text, string terminator)
    {
        var (kind, key, value) = Classify(text);
        return new IniLine(text, terminator, kind, key, value);
    }

    private static (IniLineKind Kind, string? Key, string? Value) Classify(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
        {
            return (IniLineKind.Blank, null, null);
        }
        if (trimmed[0] is ';' or '#')
        {
            return (IniLineKind.Comment, null, null);
        }
        if (trimmed[0] == '[')
        {
            var close = trimmed.TrimEnd();
            if (close.Length >= 2 && close[^1] == ']')
            {
                return (IniLineKind.SectionHeader, close[1..^1], null);
            }
            return (IniLineKind.Other, null, null);
        }

        var eq = text.IndexOf('=', StringComparison.Ordinal);
        if (eq > 0)
        {
            return (IniLineKind.KeyValue, text[..eq].Trim(), text[(eq + 1)..]);
        }
        return (IniLineKind.Other, null, null);
    }
}
