using System.Text;

namespace AbioticEditor.Core.Ini;

/// <summary>
/// Order- and comment-preserving parser/writer for UE-style <c>.ini</c> files
/// (<c>[Sections]</c>, <c>Key=Value</c>, duplicate keys legal - UE appends with
/// <c>+Key=...</c> and the game's own <c>Admin.ini</c> repeats <c>Moderator=</c> lines).
///
/// The model is line-based: every line keeps its raw text and its own terminator
/// (<c>\r\n</c> / <c>\n</c> / <c>\r</c> / none on the final line), so a file that is
/// loaded and saved without edits round-trips byte-identical - including mixed line
/// endings (the game's <c>SandboxSettings.ini</c> mixes CRLF and LF), blank lines,
/// <c>;</c>/<c>#</c> comments, and unrecognized constructs, which are all preserved
/// verbatim. Encoding (UTF-8 / UTF-16, with or without BOM) is detected on load and
/// reused on save; non-UTF-8 byte sequences fall back to Latin-1 so no byte is lost.
///
/// Edits only rewrite the value portion of the targeted <c>Key=Value</c> line (the key
/// text and anything before the first <c>=</c> stay verbatim); new lines adopt the
/// file's dominant newline. Section and key lookups are case-insensitive, matching UE
/// config semantics.
/// </summary>
public sealed class IniFile
{
    private readonly List<IniLine> _lines;
    private readonly Encoding _encoding;
    private readonly bool _hasBom;
    private readonly string _newLine;

    private IniFile(List<IniLine> lines, Encoding encoding, bool hasBom, string newLine)
    {
        _lines = lines;
        _encoding = encoding;
        _hasBom = hasBom;
        _newLine = newLine;
    }

    /// <summary>The newline new lines are written with (the file's dominant terminator).</summary>
    public string NewLine => _newLine;

    // ---------- load / parse ----------

    /// <summary>Loads <paramref name="path"/>, detecting encoding/BOM from the bytes.</summary>
    public static IniFile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var bytes = File.ReadAllBytes(path);
        var (encoding, hasBom) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, hasBom ? encoding.GetPreamble().Length : 0,
            bytes.Length - (hasBom ? encoding.GetPreamble().Length : 0));
        return ParseCore(text, encoding, hasBom);
    }

    /// <summary>Parses in-memory text (saved files use UTF-8 without BOM).</summary>
    public static IniFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseCore(text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), hasBom: false);
    }

    private static IniFile ParseCore(string text, Encoding encoding, bool hasBom)
    {
        var lines = new List<IniLine>();
        int crlf = 0, lf = 0, cr = 0;
        var start = 0;
        while (start <= text.Length)
        {
            if (start == text.Length)
            {
                // A trailing terminator yields no phantom empty line; an empty file
                // yields a single terminator-less empty line only if truly empty.
                if (text.Length == 0)
                {
                    lines.Add(IniLine.FromRaw(string.Empty, string.Empty));
                }
                break;
            }

            var i = start;
            while (i < text.Length && text[i] != '\r' && text[i] != '\n')
            {
                i++;
            }

            string terminator;
            int next;
            if (i == text.Length)
            {
                terminator = string.Empty;
                next = text.Length + 1; // force loop exit after this line
            }
            else if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                terminator = "\r\n";
                next = i + 2;
                crlf++;
            }
            else if (text[i] == '\r')
            {
                terminator = "\r";
                next = i + 1;
                cr++;
            }
            else
            {
                terminator = "\n";
                next = i + 1;
                lf++;
            }

            lines.Add(IniLine.FromRaw(text[start..i], terminator));
            start = next;
        }

        // Dominant terminator decides what NEW lines get; UE's default is CRLF.
        var newLine = "\r\n";
        if (lf > crlf && lf >= cr) newLine = "\n";
        else if (cr > crlf && cr > lf) newLine = "\r";

        return new IniFile(lines, encoding, hasBom, newLine);
    }

    private static (Encoding Encoding, bool HasBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), true);
        }

        // No BOM: strict UTF-8 if the bytes are valid, otherwise Latin-1 (a lossless
        // byte↔char mapping) so unknown legacy bytes survive the round trip.
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            strict.GetString(bytes);
            return (strict, false);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1, false);
        }
    }

    // ---------- save ----------

    /// <summary>The full file content as text (terminators included).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.Append(line.Text).Append(line.Terminator);
        }
        return sb.ToString();
    }

    /// <summary>Writes the file with the encoding/BOM it was loaded with.</summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var body = _encoding.GetBytes(ToText());
        if (_hasBom)
        {
            var preamble = _encoding.GetPreamble();
            var all = new byte[preamble.Length + body.Length];
            preamble.CopyTo(all, 0);
            body.CopyTo(all, preamble.Length);
            File.WriteAllBytes(path, all);
            return;
        }
        File.WriteAllBytes(path, body);
    }

    // ---------- sections ----------

    /// <summary>
    /// All sections in file order. Index 0 is the unnamed preamble section (keys before
    /// the first <c>[header]</c>) only when such content exists.
    /// </summary>
    public IReadOnlyList<IniSection> Sections
    {
        get
        {
            var result = new List<IniSection>();
            var sawHeader = false;
            foreach (var line in _lines)
            {
                if (line.Kind == IniLineKind.SectionHeader)
                {
                    result.Add(new IniSection(this, line));
                    sawHeader = true;
                }
                else if (!sawHeader && result.Count == 0 && line.Kind == IniLineKind.KeyValue)
                {
                    result.Insert(0, new IniSection(this, headerLine: null));
                }
            }
            return result;
        }
    }

    /// <summary>Case-insensitive section lookup; empty/null name = the preamble.</summary>
    public IniSection? FindSection(string? name)
        => Sections.FirstOrDefault(s => string.Equals(s.Name, name ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds <paramref name="name"/> or appends a new <c>[name]</c> section at the end
    /// of the file (separated by a blank line, matching the game's own layout).
    /// </summary>
    public IniSection GetOrAddSection(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (FindSection(name) is { } existing)
        {
            return existing;
        }

        EnsureTrailingTerminator();
        if (_lines.Count > 0 && _lines[^1].Kind != IniLineKind.Blank)
        {
            _lines.Add(IniLine.FromRaw(string.Empty, _newLine));
        }
        var header = IniLine.FromRaw($"[{name}]", _newLine);
        _lines.Add(header);
        return new IniSection(this, header);
    }

    // ---------- internals shared with IniSection ----------

    internal List<IniLine> Lines => _lines;

    /// <summary>The last line must own a terminator before anything is appended after it.</summary>
    internal void EnsureTrailingTerminator()
    {
        if (_lines.Count > 0 && _lines[^1].Terminator.Length == 0)
        {
            _lines[^1].Terminator = _newLine;
        }
    }

    /// <summary>
    /// The line index range [start, end) of a section's body: from just after its header
    /// to the next header (or EOF). The preamble (null header) spans from 0. The header
    /// is matched by reference so duplicate <c>[Name]</c> lines stay distinct.
    /// </summary>
    internal (int Start, int End) BodyRange(IniLine? headerLine)
    {
        var start = 0;
        if (headerLine is not null)
        {
            var headerIndex = -1;
            for (var i = 0; i < _lines.Count; i++)
            {
                if (ReferenceEquals(_lines[i], headerLine))
                {
                    headerIndex = i;
                    break;
                }
            }
            if (headerIndex < 0)
            {
                return (0, 0); // stale section view after the header was removed
            }
            start = headerIndex + 1;
        }

        var end = start;
        while (end < _lines.Count && _lines[end].Kind != IniLineKind.SectionHeader)
        {
            end++;
        }
        return (start, end);
    }
}

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

/// <summary>
/// Live view over one section of an <see cref="IniFile"/>. Enumerations reflect the
/// current file state; mutations edit the parent file in place keeping duplicate keys
/// in their original order.
/// </summary>
public sealed class IniSection
{
    private readonly IniFile _file;
    private readonly IniLine? _headerLine;

    internal IniSection(IniFile file, IniLine? headerLine)
    {
        _file = file;
        _headerLine = headerLine;
    }

    /// <summary>Section name; empty string for the unnamed preamble.</summary>
    public string Name => _headerLine?.Key ?? string.Empty;

    /// <summary>All keys in order, duplicates included.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            var (start, end) = _file.BodyRange(_headerLine);
            for (var i = start; i < end; i++)
            {
                if (_file.Lines[i].Kind == IniLineKind.KeyValue)
                {
                    yield return _file.Lines[i].Key!;
                }
            }
        }
    }

    /// <summary>All <c>(Key, Value)</c> pairs in order, duplicates included.</summary>
    public IEnumerable<KeyValuePair<string, string>> Entries
    {
        get
        {
            var (start, end) = _file.BodyRange(_headerLine);
            for (var i = start; i < end; i++)
            {
                var line = _file.Lines[i];
                if (line.Kind == IniLineKind.KeyValue)
                {
                    yield return new KeyValuePair<string, string>(line.Key!, line.Value!);
                }
            }
        }
    }

    /// <summary>The FIRST value for <paramref name="key"/> (case-insensitive), or null.</summary>
    public string? GetValue(string key)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }
        return null;
    }

    /// <summary>Every value for <paramref name="key"/> in file order (duplicate-key form).</summary>
    public IReadOnlyList<string> GetValues(string key)
        => Entries.Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                  .Select(e => e.Value)
                  .ToList();

    /// <summary>
    /// Replaces the FIRST occurrence's value in place (key text and spacing before the
    /// <c>=</c> stay verbatim), or appends <c>key=value</c> at the end of the section
    /// when the key is absent.
    /// </summary>
    public void SetValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var (start, end) = _file.BodyRange(_headerLine);
        for (var i = start; i < end; i++)
        {
            var line = _file.Lines[i];
            if (line.Kind == IniLineKind.KeyValue
                && string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                ReplaceValueAt(i, value);
                return;
            }
        }
        AddValue(key, value);
    }

    /// <summary>
    /// Appends a NEW <c>key=value</c> line - after the last existing occurrence of the
    /// key when present (keeping duplicate runs contiguous, like UE's <c>+Key=</c>
    /// accumulation), otherwise after the section's last non-blank line.
    /// </summary>
    public void AddValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var (start, end) = _file.BodyRange(_headerLine);
        var insertAt = -1;
        for (var i = start; i < end; i++)
        {
            var line = _file.Lines[i];
            if (line.Kind == IniLineKind.KeyValue
                && string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                insertAt = i + 1;
            }
        }
        if (insertAt < 0)
        {
            // After the last contentful line so the blank separator before the next
            // section stays at the section boundary.
            insertAt = start;
            for (var i = start; i < end; i++)
            {
                if (_file.Lines[i].Kind != IniLineKind.Blank)
                {
                    insertAt = i + 1;
                }
            }
        }

        if (insertAt >= _file.Lines.Count)
        {
            _file.EnsureTrailingTerminator();
            insertAt = _file.Lines.Count;
        }
        _file.Lines.Insert(insertAt, IniLine.FromRaw($"{key}={value}", _file.NewLine));
    }

    /// <summary>Removes the first line matching key AND value (both case-insensitive key, ordinal value).</summary>
    public bool RemoveValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var (start, end) = _file.BodyRange(_headerLine);
        for (var i = start; i < end; i++)
        {
            var line = _file.Lines[i];
            if (line.Kind == IniLineKind.KeyValue
                && string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(line.Value, value, StringComparison.Ordinal))
            {
                _file.Lines.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Removes every occurrence of <paramref name="key"/>; returns how many lines went.</summary>
    public int RemoveKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var removed = 0;
        var (start, end) = _file.BodyRange(_headerLine);
        for (var i = end - 1; i >= start; i--)
        {
            var line = _file.Lines[i];
            if (line.Kind == IniLineKind.KeyValue
                && string.Equals(line.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                _file.Lines.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    private void ReplaceValueAt(int lineIndex, string value)
    {
        var line = _file.Lines[lineIndex];
        var eq = line.Text.IndexOf('=', StringComparison.Ordinal);
        // Keep everything up to and including '=' byte-for-byte; only the value changes.
        _file.Lines[lineIndex] = IniLine.FromRaw(line.Text[..(eq + 1)] + value, line.Terminator);
    }
}
