using AbioticEditor.Core.Assets;
using Newtonsoft.Json.Linq;

namespace AbioticEditor.Core.Ini;

public sealed record SandboxSettingOption(string Label, string Value);
public sealed record SandboxSettingDefinition(string Key, string Label, string Description, string Category,
    string DefaultValue, string DataType, string Minimum, string Maximum, IReadOnlyList<SandboxSettingOption> Options);

/// <summary>Settings supported by the installed game's DT_SandboxOptions table.</summary>
public static class SandboxSettingCatalog
{
    public static IReadOnlyList<SandboxSettingDefinition> Load(GameAssetProvider provider)
    {
        var table = provider.TryLoadDataTable("AbioticFactor/Content/Blueprints/DataTables/DT_SandboxOptions");
        if (table is null) return [];
        var rows = JObject.FromObject(table)["Rows"] as JObject;
        if (rows is null) return [];
        return rows.Properties().Where(row => row.Value["bUnimplemented"]?.Value<bool>() != true)
            .Select(row => new SandboxSettingDefinition(row.Name,
                Text(row.Value["DisplayName"]), Text(row.Value["DisplayDescription"]), Text(row.Value["SubCategory"]),
                (string?)row.Value["DefaultValue"] ?? "", (string?)row.Value["DataType"] ?? "",
                (string?)row.Value["MinimumValue"] ?? "", (string?)row.Value["MaximumValue"] ?? "",
                row.Value["Options"]?.Select(option => new SandboxSettingOption(Text(option["Label"]), (string?)option["Value"] ?? "")).ToArray() ?? []))
            .Where(setting => setting.DefaultValue.Length > 0 && setting.Label.Length > 0)
            .OrderBy(setting => setting.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Text(JToken? token) => (string?)token?["LocalizedString"] ?? (string?)token?["SourceString"] ?? "";
}
