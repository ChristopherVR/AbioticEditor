using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Real, per-placed-actor names for <c>NarrativeNPCMap</c> entries.
///
/// Supersedes the "anonymous slot" verdict in
/// <c>docs/reference/research/research-narrative-npcs.md</c>: every placed <c>NarrativeNPC_*</c>
/// actor DOES carry its own <c>NarrativeNPC_ConversationRow</c> (set per level instance, not just
/// the class default), which names the exact character occupying that slot via
/// <c>DT_NPC_Conversations</c>'s own <c>NPCName</c> - the level files always knew this; nothing
/// in the save file needed to. Confirmed against a real install by
/// <c>tests/AbioticEditor.Probes/NarrativeNpcLevelProbe.cs</c> (e.g. in <c>Facility_Pens</c>,
/// <c>NarrativeNPC_Ela_C_1</c> -&gt; row <c>Labs_Ela_Pest</c> -&gt; "Ela";
/// <c>NarrativeNPC_Human_Hologram_C_0</c> -&gt; row <c>Manse_DL_03</c> -&gt; "Dr. Manse").
///
/// <see cref="BuildFrom"/> is <c>DoorLocationResolver</c>'s counterpart for names instead of
/// positions, but built ONCE across every level (77 <c>.umap</c> files, ~85s measured) rather than
/// resolved lazily per map - the whole point is to bundle the result into the game-data registry
/// so NEITHER host loads level packages at runtime. Call it only from a maintainer tool
/// (<c>GameDataRegistry.BuildFromInstall</c> / <c>dump-registry</c>); never from the running app.
/// </summary>
public static class NarrativeNpcNameCatalog
{
    private const string ConversationsTable = "AbioticFactor/Content/Blueprints/DataTables/DT_NPC_Conversations";
    private const string MapsRoot = "AbioticFactor/Content/Maps/";

    /// <summary>
    /// The composite key a resolved dictionary is keyed by: the level's own base file name (no
    /// path, no extension, e.g. <c>Facility_Pens</c>) and the placed actor's instance name (e.g.
    /// <c>NarrativeNPC_Ela_C_1</c>), exactly what <see cref="DoorIdParser.Parse"/> - the same
    /// generic UE actor-path parser <c>WorldDoorsTab</c> already relies on for <c>WorldDoor.Id</c>,
    /// reused unchanged here since <c>WorldNpc.Id</c> is the identical actor-path shape - returns
    /// as (Map, Actor) for a <c>NarrativeNPCMap</c> key.
    /// </summary>
    public static string KeyFor(string mapName, string actorName) => $"{mapName}:{actorName}";

    /// <summary>
    /// Parses a <c>WorldNpc.Id</c> (file form <c>/Game/Maps/X.X:PersistentLevel.Actor_C_1</c>, or
    /// the live <c>GetFullName()</c> form <c>"ClassName /Game/Maps/X.X:PersistentLevel.Actor_C_1"</c>
    /// - <see cref="DoorIdParser"/> already accepts both) into the same key <see cref="BuildFrom"/>
    /// resolved names against, or null when the id doesn't parse to a (map, actor) pair.
    /// </summary>
    public static string? KeyForActorPath(string? actorPath)
    {
        if (string.IsNullOrEmpty(actorPath)) return null;
        var (map, actor) = DoorIdParser.Parse(actorPath);
        return map.Length == 0 || actor.Length == 0 ? null : KeyFor(map, actor);
    }

    /// <summary>
    /// Looks a <c>WorldNpc.Id</c> up against a resolved dictionary (typically
    /// <see cref="GameDataRegistry.NarrativeNpcNames"/>), returning null when nothing matches -
    /// the id doesn't parse, no level carries that actor, or that actor has no conversation row.
    /// </summary>
    public static string? Resolve(IReadOnlyDictionary<string, string>? names, string? actorPath)
    {
        if (names is null || names.Count == 0) return null;
        var key = KeyForActorPath(actorPath);
        return key is not null && names.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    /// <summary>
    /// Walks every level package under <c>AbioticFactor/Content/Maps</c> for placed
    /// <c>NarrativeNPC_*</c> actors, resolving each one's <c>NarrativeNPC_ConversationRow</c>
    /// against <c>DT_NPC_Conversations</c>. Never throws; a level or table that fails to load is
    /// skipped (logged), matching this codebase's graceful-degradation rule for every other
    /// catalog - a renamed map or table in a future patch must not take down the whole dump.
    /// SLOW (~85s against the full install) - dump-time only, see this class's own remarks.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildFrom(GameAssetProvider provider)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conversationNames = LoadConversationNames(provider);
        if (conversationNames.Count == 0) return result;

        foreach (var path in provider.AssetPaths.Where(p =>
                     p.StartsWith(MapsRoot, StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)))
        {
            var mapName = System.IO.Path.GetFileNameWithoutExtension(path);
            try
            {
                var pkg = provider.LoadPackageInternal(path);
                foreach (var lazy in pkg.ExportsLazy)
                {
                    CUE4Parse.UE4.Assets.Exports.UObject? export;
                    try { export = lazy.Value; }
                    catch { continue; } // tolerate per-export deserialization failures

                    if (export is null || !export.Name.StartsWith("NarrativeNPC_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rowTag = export.Properties.FirstOrDefault(p => p.Name.Text == "NarrativeNPC_ConversationRow");
                    var row = RowNameOf(rowTag?.Tag?.GenericValue);
                    if (row is null) continue;
                    if (!conversationNames.TryGetValue(row, out var name) || string.IsNullOrWhiteSpace(name)) continue;

                    result[KeyFor(mapName, export.Name)] = name;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("NarrativeNpcNames", $"Could not load {path}: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>Row name -&gt; <c>NPCName</c> (localized text, matching the mounted culture) from
    /// <c>DT_NPC_Conversations</c> - the same table/field <c>GameAssetProvider.
    /// TryGetNarrativeCharacterName</c> already reads for a single live actor.</summary>
    private static Dictionary<string, string> LoadConversationNames(GameAssetProvider provider)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var table = provider.TryLoadDataTable(ConversationsTable);
        if (table is null) return result;
        foreach (var kv in table.RowMap)
        {
            var name = kv.Value.Properties
                .FirstOrDefault(p => p.Name.Text == "NPCName")?.Tag?.GenericValue?.ToString();
            if (!string.IsNullOrWhiteSpace(name)) result[kv.Key.Text] = name!;
        }
        return result;
    }

    /// <summary>The <c>RowName</c> inside a DataTable-handle struct (<c>{DataTable, RowName}</c>),
    /// or null / "None"-as-null - same shape/idiom as <c>TraderCatalog.RowNameOf</c>.</summary>
    private static string? RowNameOf(object? value)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback sf) return null;
        var v = sf.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
        return string.IsNullOrEmpty(v) || v == "None" ? null : v;
    }
}
