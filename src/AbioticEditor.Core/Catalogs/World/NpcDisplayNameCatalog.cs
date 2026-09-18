using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Class -> friendly in-game display name for every NPC the game spawns, read from
/// <c>DT_NPCList</c> - the same table <see cref="PetGameData"/> already reads for tameable
/// companions alone (row -> display name + <c>NPCSpawnClass</c>), generalized here to every row
/// so a live "creature" listing (<c>NPC_Base_ParentBP_C</c>, wildlife/monsters/humanoid NPCs
/// alike) can show the name the game itself uses instead of a guess derived from the class name.
///
/// That guess (stripping a known prefix, then title-casing what remains - see the old
/// <c>LiveNpcsTab.razor</c>'s own <c>DisplayName</c>, now folded into the merged NPCS tab as the
/// wiki-lookup key) is not always right: <c>NPC_Robot_Defense_C</c> derives to "Robot Defense",
/// but <c>DT_NPCList</c>'s own row calls it "Defense Robot" (see
/// <c>tests/AbioticEditor.Probes/NpcRosterProbe.cs</c> and <c>CreatureWikiImages</c>'s header
/// comment for how that mismatch was found). Bundled into the game-data registry
/// (<see cref="GameDataRegistry.NpcDisplayNames"/>) so a browser build with no game install
/// resolves the same names a mounted desktop install would.
/// </summary>
public static class NpcDisplayNameCatalog
{
    private const string NpcListTable = "AbioticFactor/Content/Blueprints/DataTables/DT_NPCList";

    /// <summary>
    /// Reads every <c>DT_NPCList</c> row (plus any mod/patch table merged into it) into a
    /// short-class-name -> display-name dictionary. Never throws; an unreadable table yields an
    /// empty dictionary, matching this codebase's graceful-degradation rule for every other
    /// catalog.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadFrom(GameAssetProvider provider)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var primary = provider.TryLoadDataTable(NpcListTable);
            AddRows(primary, result);
            foreach (var extra in ModTableDiscovery.LoadTablesByRowStruct(provider, primary?.RowStructName, new[] { NpcListTable }))
            {
                AddRows(extra, result);
            }
        }
        catch
        {
            return result;
        }
        return result;
    }

    private static void AddRows(UDataTable? dt, Dictionary<string, string> result)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            string? name = null, spawnClass = null;
            foreach (var p in kv.Value.Properties)
            {
                var n = p.Name.Text;
                if (n.StartsWith("DisplayName_", StringComparison.Ordinal))
                {
                    name = p.Tag?.GenericValue?.ToString();
                }
                else if (n.StartsWith("NPCSpawnClass_", StringComparison.Ordinal))
                {
                    spawnClass = p.Tag?.GenericValue?.ToString();
                }
            }
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Key by the spawned class's short name (matches a live "Label" or a soft class
            // path alike, the same normalization PetCatalog.ByClass already relies on), falling
            // back to the row name itself when the row carries no spawn class.
            var key = PetCatalog.ShortOf(spawnClass);
            if (key.Length == 0) key = kv.Key.Text;
            if (key.Length > 0 && !result.ContainsKey(key)) result[key] = name!;
        }
    }

    /// <summary>
    /// Looks a class path or short class name up against a resolved dictionary (typically
    /// <see cref="GameDataRegistry.NpcDisplayNames"/> or a live <see cref="LoadFrom"/> result),
    /// returning null when nothing matches.
    /// </summary>
    public static string? Resolve(IReadOnlyDictionary<string, string>? names, string? classOrShort)
    {
        if (names is null || names.Count == 0) return null;
        var shortClass = PetCatalog.ShortOf(classOrShort);
        return shortClass.Length > 0 && names.TryGetValue(shortClass, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }
}
