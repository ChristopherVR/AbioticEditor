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

        // No BOM, but a NUL byte never appears in real UTF-8/Latin-1 ini text - it is the
        // tell-tale of BOM-less UTF-16 (ASCII interleaved with zero high/low bytes). Detect it
        // by which parity of byte positions is mostly zero, so a BOM-less UTF-16 file isn't
        // misread as UTF-8 (NUL is valid UTF-8) and corrupted on save. UE normally emits a BOM
        // for UTF-16, so this is a belt-and-suspenders guard.
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            int evenNul = 0, oddNul = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0) continue;
                if ((i & 1) == 0) evenNul++; else oddNul++;
            }
            // ASCII in UTF-16LE zeroes the high byte (odd index); UTF-16BE zeroes the low byte.
            return oddNul >= evenNul
                ? (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), false)
                : (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), false);
        }

        // strict UTF-8 if the bytes are valid, otherwise Latin-1 (a lossless byte<->char
        // mapping) so unknown legacy bytes survive the round trip.
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
                else if (!sawHeader && result.Count == 0 && line.Kind != IniLineKind.Blank)
                {
                    // Any non-blank content before the first [header] forms the preamble section,
                    // not just a bare Key=Value. A comment- or stray-content-led preamble is then
                    // addressable via FindSection(null) so keys can be read/added there.
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
