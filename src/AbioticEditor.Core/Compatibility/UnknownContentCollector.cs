using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Core.Compatibility;

/// <summary>One unknown-data observation: the same (area, key, context) triple that the UNKWN log channel carries.</summary>
public readonly record struct UnknownDataEntry(string Area, string Key, string? Context);

/// <summary>
/// In-memory capture of everything the readers report on the
/// <see cref="EditorLog.UnknownData"/> channel - unmodeled property keys, unknown enum
/// values, unknown door classes, etc. Works regardless of whether file logging is
/// enabled, so a <see cref="CompatibilityReport"/> can count unknown content even with
/// diagnostics off.
///
/// Usage: <c>using var scope = UnknownContentCollector.Begin();</c> around the save read,
/// then hand the collector to <see cref="CompatibilityAnalyzer"/>. Entries are
/// deduplicated per (area, key) like the log channel itself.
/// </summary>
public sealed class UnknownContentCollector : IDisposable
{
    /// <summary>
    /// UNKWN areas whose keys are top-level save property names (the readers' unmodeled
    /// key channels). Everything else on the channel is an unknown vocabulary/enum value.
    /// </summary>
    private static readonly string[] PropertyKeyAreas = { "WorldSave", "PlayerSave", "Customization" };

    private readonly object _sync = new();
    private readonly List<UnknownDataEntry> _entries = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private bool _disposed;

    private UnknownContentCollector()
    {
    }

    /// <summary>Starts collecting; dispose to stop. Nesting/parallel scopes are safe (each observes independently).</summary>
    public static UnknownContentCollector Begin()
    {
        var collector = new UnknownContentCollector();
        EditorLog.UnknownDataObserved += collector.OnUnknownData;
        return collector;
    }

    /// <summary>Everything observed so far, deduplicated per (area, key), in observation order.</summary>
    public IReadOnlyList<UnknownDataEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Observed unmodeled top-level property keys (reader UNKWN areas), formatted "Area: Key".</summary>
    public IReadOnlyList<string> UnknownPropertyKeys
        => Partition(propertyKeys: true);

    /// <summary>Observed unknown enum/vocabulary values (all non-reader UNKWN areas), formatted "Area: Key".</summary>
    public IReadOnlyList<string> UnknownEnumValues
        => Partition(propertyKeys: false);

    private List<string> Partition(bool propertyKeys)
    {
        lock (_sync)
        {
            var result = new List<string>();
            foreach (var entry in _entries)
            {
                var isPropertyKey = PropertyKeyAreas.Contains(entry.Area, StringComparer.OrdinalIgnoreCase);
                if (isPropertyKey == propertyKeys)
                {
                    result.Add($"{entry.Area}: {entry.Key}");
                }
            }
            return result;
        }
    }

    private void OnUnknownData(string area, string key, string? context)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (_seen.Add($"{area}|{key}"))
            {
                _entries.Add(new UnknownDataEntry(area, key, context));
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        EditorLog.UnknownDataObserved -= OnUnknownData;
    }
}
