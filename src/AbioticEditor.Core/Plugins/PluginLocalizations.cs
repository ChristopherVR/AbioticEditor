namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Process-wide table of plugin-contributed UI translations, layered on top of the app's
/// built-in <c>AppResources.resx</c>. Populated as plugins load: a resource-only
/// <c>localization</c>-runtime pack (resx/json files), or a .NET/JavaScript
/// plugin that calls <c>IPluginRegistry.AddLocalization</c>. The GUI's localization manager
/// consults <see cref="Lookup"/> before its built-in table, so a plugin can add an entire new
/// language or override individual keys. Always present (the CLI/tests just hold whatever was
/// registered and never read it).
/// </summary>
public static class PluginLocalizations
{
    private static readonly object Sync = new();

    // culture (lower-invariant) -> key -> value.
    private static readonly Dictionary<string, Dictionary<string, string>> ByCulture =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after strings are added or cleared, so bound UI can re-resolve.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Merges <paramref name="strings"/> (key -> translated text) for <paramref name="culture"/>.
    /// Last write wins, so a later plugin can override an earlier one. No-op for an empty set.
    /// </summary>
    public static void Add(string culture, IReadOnlyDictionary<string, string> strings)
    {
        if (string.IsNullOrWhiteSpace(culture) || strings is null || strings.Count == 0)
        {
            return;
        }

        var code = Normalize(culture);
        lock (Sync)
        {
            if (!ByCulture.TryGetValue(code, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                ByCulture[code] = table;
            }
            foreach (var kv in strings)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                {
                    table[kv.Key] = kv.Value ?? string.Empty;
                }
            }
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The plugin-contributed value for <paramref name="key"/> in <paramref name="culture"/>, or
    /// null when no plugin supplies it. Tries the exact culture, then its neutral parent
    /// (so <c>de-DE</c> falls back to a pack that shipped strings for <c>de</c>).
    /// </summary>
    public static string? Lookup(string culture, string key)
    {
        if (string.IsNullOrEmpty(culture) || string.IsNullOrEmpty(key))
        {
            return null;
        }

        lock (Sync)
        {
            if (ByCulture.Count == 0)
            {
                return null;
            }

            var code = Normalize(culture);
            if (ByCulture.TryGetValue(code, out var exact) && exact.TryGetValue(key, out var value))
            {
                return value;
            }

            var dash = code.IndexOf('-');
            if (dash > 0
                && ByCulture.TryGetValue(code[..dash], out var parent)
                && parent.TryGetValue(key, out var parentValue))
            {
                return parentValue;
            }
        }
        return null;
    }

    /// <summary>Culture codes that currently have at least one contributed string.</summary>
    public static IReadOnlyCollection<string> Cultures
    {
        get { lock (Sync) { return ByCulture.Keys.ToArray(); } }
    }

    /// <summary>Drops every contributed string (used by tests and a plugin reload).</summary>
    public static void Clear()
    {
        lock (Sync)
        {
            if (ByCulture.Count == 0)
            {
                return;
            }
            ByCulture.Clear();
        }
        Changed?.Invoke();
    }

    private static string Normalize(string culture) => culture.Trim().ToLowerInvariant();
}
