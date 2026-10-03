using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Convenience base for the common map shape (entries of
/// <c>StructProperty -&gt; PropertiesStruct</c>). A concrete feature only supplies its metadata
/// plus <see cref="ReadFields"/> (entry struct → typed fields) and <see cref="ApplyField(IList{FPropertyTag}, string, string)"/>
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
        OnBeginRead(save);
        var list = new List<WorldMapEntry>();
        var ordinal = 0;
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            if (!IncludesEntry(entry.Props)) continue;
            ordinal++;
            var (linkId, linkLabel, needsHost) = LinkFor(entry.Key, entry.Props);
            list.Add(new WorldMapEntry(
                entry.Key, LabelFor(ordinal, entry.Key, entry.Props), ReadFields(entry.Props),
                linkId, linkLabel, needsHost));
        }
        return list;
    }

    /// <summary>
    /// Hook called once at the start of each <see cref="Read"/>, before any entry is processed.
    /// Lets a feature build a per-read cache (e.g. a device index) that <see cref="ReadFields"/>
    /// and <see cref="LinkFor"/> then use. Default does nothing.
    /// </summary>
    protected virtual bool IncludesEntry(IList<FPropertyTag> props) => true;

    protected virtual void OnBeginRead(SaveGame save)
    {
    }

    /// <summary>
    /// Optional link from one entry to another editable entity (e.g. the container a power socket
    /// powers): returns its target id and a button label, or (null, null) for no link. Default none.
    /// </summary>
    protected virtual (string? TargetId, string? Label, bool NeedsHostResolution) LinkFor(
        string key, IList<FPropertyTag> props)
        => (null, null, false);

    public WorldEditResult SetField(SaveGame save, string entryKey, string fieldId, string? value)
    {
        ArgumentNullException.ThrowIfNull(save);
        var props = WorldMapAccessor.FindEntry(save, MapName, entryKey);
        if (props is null && CanReadDefaultActor(entryKey))
        {
            var pair = DefaultActorPair(save, entryKey);
            if (pair is not { Value: StructProperty { Value: UeSaveGame.StructData.PropertiesStruct fresh } })
                return WorldEditResult.Failure("This area's save has no template for this object yet. Use it in the game first.");
            var result = ApplyField(save, fresh.Properties, fieldId, value);
            if (result.Changed) WorldMapAccessor.GetPairs(save, MapName)!.Add(pair.Value);
            return result;
        }
        if (props is null || !IncludesEntry(props))
        {
            return WorldEditResult.Failure($"no entry '{entryKey}' in {MapName}.");
        }
        return ApplyField(save, props, fieldId, value);
    }

    /// <summary>A level actor may still be at its default and therefore absent from the save.</summary>
    public WorldMapEntry? ReadActor(SaveGame save, string key)
        => Read(save).FirstOrDefault(e => e.Key == key)
            ?? (CanReadDefaultActor(key) ? new WorldMapEntry(key, LabelFor(1, key, []), ReadFields([])) : null);

    private bool CanReadDefaultActor(string key)
        => MapName is "DestructibleMap" or "ResourceNodeMap" or "NPCSpawnMap"
           && key.StartsWith("/Game/Maps/", StringComparison.Ordinal) && key.Contains(":PersistentLevel.", StringComparison.Ordinal);

    private KeyValuePair<FProperty, FProperty>? DefaultActorPair(SaveGame save, string key)
    {
        var donor = WorldMapAccessor.Entries(save, MapName).FirstOrDefault();
        if (donor.Props is null) return null;
        var copy = PlacedObjectCloner.CreateDonor(save, new Dictionary<string, IReadOnlySet<string>?>
        {
            [MapName] = new HashSet<string>(StringComparer.Ordinal) { donor.Key },
        });
        var pair = WorldMapAccessor.GetPairs(copy, MapName)!.First();
        pair.Key.Value = new UeSaveGame.FString(key);
        var props = ((UeSaveGame.StructData.PropertiesStruct)((UeSaveGame.PropertyTypes.StructProperty)pair.Value).Value!).Properties;
        foreach (var tag in props.ToList())
        {
            if (tag.Name?.Value.StartsWith("ActorPath_", StringComparison.Ordinal) == true
                && WorldMapAccessor.SetSoftObjectPath(props, "ActorPath_", key)) continue;
            switch (tag.Property)
            {
                case UeSaveGame.PropertyTypes.BoolProperty boolean: boolean.Value = false; break;
                case UeSaveGame.PropertyTypes.IntProperty integer: integer.Value = 0; break;
                case UeSaveGame.PropertyTypes.DoubleProperty number: number.Value = 0d; break;
                default: props.Remove(tag); break;
            }
        }
        // Optional position members are omitted rather than carrying the donor's location.
        return pair;
    }

    /// <inheritdoc/>
    public virtual bool SupportsRemoval => true;

    /// <inheritdoc/>
    public virtual string RemoveActionLabel => "Remove this Entry";

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
    /// Save-aware variant for features whose edit needs a game-authored template from elsewhere in
    /// the same save (for example planting, which clones an existing planted spot). Defaults to
    /// the entry-only overload.
    /// </summary>
    protected virtual WorldEditResult ApplyField(SaveGame save, IList<FPropertyTag> props, string fieldId, string? value)
        => ApplyField(props, fieldId, value);

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
