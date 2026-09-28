using System.Text.RegularExpressions;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.Compatibility;

/// <summary>Which family of save file a census row was collected from.</summary>
public enum CensusSaveKind
{
    /// <summary>A per-region <c>WorldSave_&lt;Region&gt;.sav</c>.</summary>
    Region,

    /// <summary>The <c>WorldSave_MetaData.sav</c> story/metadata save.</summary>
    Metadata,

    /// <summary>A <c>Player_&lt;steamid&gt;.sav</c> (members of <c>CharacterSaveData</c>).</summary>
    Player,
}

/// <summary>
/// One property name seen across a set of saves.
/// </summary>
/// <param name="Kind">Save family.</param>
/// <param name="Property">The property name with its blueprint hash suffix removed.</param>
/// <param name="TypeName">The GVAS property type as loaded (for example <c>MapProperty</c>).</param>
/// <param name="Modeled">True when a reader or a world-map feature consumes it.</param>
/// <param name="SaveCount">How many saves of this kind carried it.</param>
/// <param name="ElementCount">Total entries (map/array/set) or 1 for scalars, summed across saves.</param>
public sealed record CensusRow(
    CensusSaveKind Kind, string Property, string TypeName, bool Modeled, int SaveCount, long ElementCount);

/// <summary>
/// One leaf member seen inside the struct values of a top-level map (for example the members
/// of a <c>NarrativeNPCMap</c> entry). Lets a reviewer see fields inside an otherwise "modeled"
/// map that no reader touches.
/// </summary>
public sealed record CensusLeafRow(
    string Map, string Leaf, string TypeName, int EntryCount);

/// <summary>
/// Read-only inventory of which properties the fixtures (or any save set) actually carry, and
/// which of them the editor's readers consume. It writes nothing and never changes a save.
/// </summary>
/// <remarks>
/// Names are normalised by stripping the <c>_&lt;n&gt;_&lt;32 hex&gt;</c> blueprint-compiler suffix
/// so two game builds of the same field count as one row. "Modeled" is decided only by the
/// top-level readers' own tests (<see cref="WorldSaveReader.IsModeledTopLevelKey"/> and
/// <see cref="PlayerSaveReader.IsModeledKey"/>); a modeled map can still hold members nothing
/// reads, which is what <see cref="CollectLeaves"/> is for.
/// </remarks>
public static partial class UnmodeledFieldCensus
{
    [GeneratedRegex(@"_\d+_[0-9A-Fa-f]{32}$")]
    private static partial Regex HashSuffix();

    /// <summary>Removes the blueprint hash suffix from a property name.</summary>
    public static string Normalize(string name) => HashSuffix().Replace(name, string.Empty);

    /// <summary>
    /// Tallies every top-level property (for player saves: every <c>CharacterSaveData</c>
    /// member) across <paramref name="saves"/>, grouped by kind, name and type.
    /// </summary>
    public static IReadOnlyList<CensusRow> Collect(IEnumerable<(CensusSaveKind Kind, SaveGame Save)> saves)
    {
        ArgumentNullException.ThrowIfNull(saves);
        var tally = new Dictionary<(CensusSaveKind, string, string), (bool Modeled, int Saves, long Elements)>();

        foreach (var (kind, save) in saves)
        {
            var props = kind == CensusSaveKind.Player
                ? PlayerSaveReader.GetCharacterSaveData(save)
                : save.Properties;
            if (props is null) continue;

            foreach (var tag in props)
            {
                var raw = tag.Name?.Value;
                if (raw is null) continue;
                var name = Normalize(raw);
                var type = tag.Property?.GetType().Name ?? "?";
                var modeled = kind == CensusSaveKind.Player
                    ? PlayerSaveReader.IsModeledKey(raw)
                    : WorldSaveReader.IsModeledTopLevelKey(raw);
                var key = (kind, name, type);
                tally.TryGetValue(key, out var cur);
                tally[key] = (modeled, cur.Saves + 1, cur.Elements + ElementCount(tag.Property));
            }
        }

        return tally
            .Select(kv => new CensusRow(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.Modeled, kv.Value.Saves, kv.Value.Elements))
            .OrderBy(r => r.Kind).ThenBy(r => r.Modeled).ThenBy(r => r.Property, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Tallies the member names of every struct-valued top-level map entry across
    /// <paramref name="saves"/> (world and metadata saves only).
    /// </summary>
    public static IReadOnlyList<CensusLeafRow> CollectLeaves(IEnumerable<SaveGame> saves)
    {
        ArgumentNullException.ThrowIfNull(saves);
        var tally = new Dictionary<(string, string, string), int>();

        foreach (var save in saves)
        {
            if (save.Properties is null) continue;
            foreach (var tag in save.Properties)
            {
                if (tag.Property is not MapProperty { Value: { } pairs } || tag.Name?.Value is not { } mapName) continue;
                var map = Normalize(mapName);
                foreach (var kv in pairs)
                {
                    if (kv.Value is not StructProperty { Value: PropertiesStruct ps }) continue;
                    foreach (var leaf in ps.Properties)
                    {
                        if (leaf.Name?.Value is not { } leafName) continue;
                        var key = (map, Normalize(leafName), leaf.Property?.GetType().Name ?? "?");
                        tally[key] = tally.GetValueOrDefault(key) + 1;
                    }
                }
            }
        }

        return tally
            .Select(kv => new CensusLeafRow(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value))
            .OrderBy(r => r.Map, StringComparer.Ordinal).ThenBy(r => r.Leaf, StringComparer.Ordinal)
            .ToList();
    }

    private static long ElementCount(FProperty? p) => p switch
    {
        MapProperty { Value: { } m } => m.Count,
        ArrayProperty { Value: { } a } => a.Length,
        _ => 1,
    };
}
