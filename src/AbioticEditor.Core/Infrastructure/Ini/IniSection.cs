namespace AbioticEditor.Core.Ini;

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

    /// <summary>
    /// The LAST value for <paramref name="key"/> (case-insensitive), or null when the key is
    /// absent. UE's ini loader applies repeated <c>Key=Value</c> lines in file order, so a later
    /// occurrence overwrites an earlier one in the game's own effective config; the last line is
    /// therefore the one that actually takes effect (unlike <c>+Key=</c> accumulation into an
    /// array-typed setting, which this type does not attempt to distinguish from a plain repeat).
    /// </summary>
    public string? GetValue(string key)
    {
        string? last = null;
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                last = entry.Value;
            }
        }
        return last;
    }

    /// <summary>Every value for <paramref name="key"/> in file order (duplicate-key form).</summary>
    public IReadOnlyList<string> GetValues(string key)
        => Entries.Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                  .Select(e => e.Value)
                  .ToList();

    /// <summary>
    /// Replaces the LAST occurrence's value in place (key text and spacing before the
    /// <c>=</c> stay verbatim), or appends <c>key=value</c> at the end of the section
    /// when the key is absent. The last occurrence is the one that matters: the game
    /// applies repeated <c>Key=Value</c> lines in file order, so it is the last line's
    /// value that is actually in effect (see <see cref="GetValue"/>'s remarks) - editing
    /// an earlier duplicate would leave the game reading its own untouched last line.
    /// Earlier duplicate lines, and everything else in the file, are left byte-identical.
    /// </summary>
    public void SetValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var (start, end) = _file.BodyRange(_headerLine);
        for (var i = end - 1; i >= start; i--)
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
