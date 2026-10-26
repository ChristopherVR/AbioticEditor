using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Codex;

public sealed record QuestDefinition(string Id, string Title, string Description, string? StartFlag,
    string? CompletionFlag, int Priority, IReadOnlyList<string> SubQuests, string? WaypointTag);
public sealed record QuestLineDefinition(string Id, string Title, bool Important, string? ParentLine,
    IReadOnlyList<string> Quests);
public sealed record QuestCatalogData(IReadOnlyList<QuestDefinition> Quests, IReadOnlyList<QuestLineDefinition> Lines);
public enum QuestState { Unknown, Locked, Active, Completed }

/// <summary>The 1.5 multi-quest graph. Tracking is personal; progress is derived from world flags.</summary>
public static class QuestCatalog
{
    private const string QuestTable = "AbioticFactor/Content/Blueprints/DataTables/DT_Quests";
    private const string LineTable = "AbioticFactor/Content/Blueprints/DataTables/DT_QuestLines";

    public static QuestState StateOf(QuestDefinition quest, IReadOnlySet<string>? flags)
    {
        if (flags is null) return QuestState.Unknown;
        if (quest.CompletionFlag is { } complete && flags.Contains(complete)) return QuestState.Completed;
        // Tutorial objectives are driven by the personal server quest, with no world flags.
        // Missing flags do not mean that all of those objectives are active simultaneously.
        if (quest.StartFlag is null) return QuestState.Unknown;
        return flags.Contains(quest.StartFlag) ? QuestState.Active : QuestState.Locked;
    }

    public static QuestCatalogData? LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return null;
        var quests = ReadRows(provider, QuestTable).Select(row => new QuestDefinition(row.Id,
            Text(row.Data, "QuestText") ?? row.Id, Text(row.Data, "QuestDescription") ?? string.Empty,
            Handle(Value(row.Data, "StartWorldFlag")), Handle(Value(row.Data, "CompletionWorldFlag")),
            Value(row.Data, "DisplayOrderPriority") is int priority ? priority : 0,
            Handles(Value(row.Data, "SubQuests")), Usable(Text(row.Data, "WaypointActorTag")))).ToArray();
        var lines = ReadRows(provider, LineTable).Select(row => new QuestLineDefinition(row.Id,
            Text(row.Data, "QuestLineName") ?? string.Empty, Value(row.Data, "bImportantQuestLine") is true,
            Handle(Value(row.Data, "ParentQuestLine")), Handles(Value(row.Data, "Quests")))).ToArray();
        return quests.Length > 0 && lines.Length > 0 ? new(quests, lines) : null;
    }

    private static IEnumerable<(string Id, FStructFallback Data)> ReadRows(GameAssetProvider provider, string path)
    {
        var table = provider.TryLoadDataTable(path);
        if (table is null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[] { table }.Concat(ModTableDiscovery.LoadTablesByRowStruct(provider, table.RowStructName, [path])))
            foreach (var (key, row) in candidate.RowMap)
                if (seen.Add(key.Text)) yield return (key.Text, row);
    }

    private static object? Value(FStructFallback row, string name)
        => row.Properties.FirstOrDefault(p => p.Name.Text.MatchesUnrealProperty(name + "_"))?.Tag?.GenericValue;
    private static string? Text(FStructFallback row, string name) => Value(row, name)?.ToString();
    private static string? Usable(string? value) => string.IsNullOrWhiteSpace(value) || value == "None" ? null : value;
    private static string? Handle(object? value)
        => (value is FScriptStruct script ? script.StructType : value) is FStructFallback row ? Usable(Text(row, "RowName")) : null;
    private static string[] Handles(object? value)
        => value is UScriptArray array ? array.Properties.Select(p => Handle(p.GenericValue)).OfType<string>().ToArray() : [];
}
