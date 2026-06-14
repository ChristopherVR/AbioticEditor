using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Convenience base for the common map shape (entries of
/// <c>StructProperty -&gt; PropertiesStruct</c>). A concrete feature only supplies its metadata
/// plus <see cref="ReadFields"/> (entry struct → typed fields) and <see cref="ApplyField"/>
/// (patch one field); this base wires the <see cref="IWorldMapFeature"/> plumbing - entry
/// enumeration, key→entry lookup, and a readable per-entry label.
/// </summary>
public abstract class WorldMapFeatureBase : IWorldMapFeature
{
    public abstract string Id { get; }

    public abstract string MapName { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public virtual bool AppliesTo(SaveGame save) => WorldMapAccessor.HasMap(save, MapName);

    public IReadOnlyList<WorldMapEntry> Read(SaveGame save)
    {
        var list = new List<WorldMapEntry>();
        var ordinal = 0;
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            ordinal++;
            list.Add(new WorldMapEntry(entry.Key, LabelFor(ordinal, entry.Key, entry.Props), ReadFields(entry.Props)));
        }
        return list;
    }

    public WorldEditResult SetField(SaveGame save, string entryKey, string fieldId, string? value)
    {
        ArgumentNullException.ThrowIfNull(save);
        var props = WorldMapAccessor.FindEntry(save, MapName, entryKey);
        if (props is null)
        {
            return WorldEditResult.Failure($"no entry '{entryKey}' in {MapName}.");
        }
        return ApplyField(props, fieldId, value);
    }

    /// <inheritdoc/>
    public virtual bool SupportsRemoval => true;

    /// <inheritdoc/>
    public virtual WorldEditResult Remove(SaveGame save, string entryKey)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (!SupportsRemoval)
        {
            return WorldEditResult.Failure($"{DisplayName} entries can't be removed.");
        }
        return WorldMapAccessor.RemoveEntry(save, MapName, entryKey)
            ? WorldEditResult.Success
            : WorldEditResult.Failure($"no entry '{entryKey}' in {MapName}.");
    }

    /// <summary>Reads one entry's struct into the typed fields shown to the user.</summary>
    protected abstract IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props);

    /// <summary>Patches one field of one entry's struct. Validate here; never throw.</summary>
    protected abstract WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value);

    /// <summary>
    /// A short, readable name for an entry. Override the ordinal overload for map-specific
    /// labels; the 1-based <paramref name="ordinal"/> lets GUID-keyed maps (no actor name in the
    /// key) number their entries (e.g. "Power Socket 3").
    /// </summary>
    protected virtual string LabelFor(int ordinal, string key, IList<FPropertyTag> props)
        => LabelFor(key, props);

    /// <summary>A short, readable name for an entry key. Override for map-specific labels.</summary>
    protected virtual string LabelFor(string key, IList<FPropertyTag> props) => ShortLabel(key);

    /// <summary>
    /// Trims an actor-path key (<c>/Game/Maps/Facility.Facility:PersistentLevel.Forklift_C_3</c>)
    /// down to the readable actor name (<c>Forklift_C_3</c>); returns short keys (GUIDs) unchanged.
    /// </summary>
    protected static string ShortLabel(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }
        var dot = key.LastIndexOf('.');
        return dot >= 0 && dot < key.Length - 1 ? key[(dot + 1)..] : key;
    }

    /// <summary>
    /// Validates a value against a choice field's option list (case-insensitive) and returns
    /// the canonical option text, or a <see cref="WorldEditResult"/> failure listing the choices.
    /// </summary>
    protected static WorldEditResult ResolveChoice(
        string? value, IReadOnlyList<string> options, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return WorldEditResult.Failure($"value required (one of: {string.Join(", ", options)}).");
        }
        foreach (var option in options)
        {
            if (string.Equals(option, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                resolved = option;
                return WorldEditResult.Success;
            }
        }
        return WorldEditResult.Failure(
            $"'{value}' is not allowed. Choose one of: {string.Join(", ", options)}.");
    }
}
