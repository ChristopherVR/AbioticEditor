using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>ServerEntitlements</c> (the metadata save): the dedicated-server,
/// per-player permission/entitlement list. The map is keyed by a player's SteamID64 and each
/// entry value is a <c>StructProperty -&gt; PropertiesStruct</c> with one member -
/// <c>Entitlements</c> (an <see cref="ArrayProperty"/> of strings) - listing the entitlements
/// (e.g. admin grants) that player holds on this server.
///
/// <para>The single-field editor model fits by treating the whole list as one
/// comma-separated text field: read joins the array with <c>", "</c>, apply splits on commas
/// and replaces the array in place (mirroring <c>WorldSaveWriter.ReplaceNameArray</c>), so the
/// rest of the entry re-serializes untouched.</para>
/// </summary>
public sealed class ServerEntitlementsFeature : WorldMapFeatureBase
{
    /// <summary>Save leaf name. The member has no blueprint hash suffix, so this is exact.</summary>
    private const string EntitlementsPrefix = "Entitlements";

    public override string Id => "server-entitlements";

    public override string MapName => "ServerEntitlements";

    public override string DisplayName => "Server Entitlements";

    public override string Description =>
        "Per-player server entitlements/permissions, keyed by SteamID64.";

    /// <summary>Entitlement entries are server metadata, not removable level actors.</summary>
    public override bool SupportsRemoval => false;

    /// <summary>The key is already a bare SteamID64; show it verbatim.</summary>
    protected override string LabelFor(string key, IList<FPropertyTag> props) => key;

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var joined = string.Join(", ", ReadEntitlements(props));
        return new[]
        {
            new WorldMapField("entitlements", "Entitlements", joined, WorldFieldKind.Text,
                Editable: true, Options: null,
                Hint: "comma-separated list of entitlement strings"),
        };
    }

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, "entitlements", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"unknown field '{fieldId}' (expected: entitlements).");
        }
        if (props.FindByPrefix(EntitlementsPrefix)?.Property is not ArrayProperty arr)
        {
            return WorldEditResult.Failure("the Entitlements array is missing from this entry.");
        }

        var parsed = ParseList(value);
        var current = ReadEntitlements(props);
        if (parsed.Count == current.Count
            && parsed.SequenceEqual(current, StringComparer.Ordinal))
        {
            return WorldEditResult.NoChange;
        }

        arr.Value = parsed.Select(s => new FString(s)).ToArray();
        return WorldEditResult.Success;
    }

    /// <summary>Reads the entry's <c>Entitlements</c> array into a string list (empty when absent).</summary>
    private static List<string> ReadEntitlements(IList<FPropertyTag> props)
    {
        var list = new List<string>();
        if (props.FindByPrefix(EntitlementsPrefix)?.Property is not ArrayProperty arr || arr.Value is null)
        {
            return list;
        }
        for (var i = 0; i < arr.Value.Length; i++)
        {
            var s = arr.Value.GetValue(i) switch
            {
                FString fs => fs.Value,
                string raw => raw,
                var v => v?.ToString(),
            };
            if (!string.IsNullOrEmpty(s))
            {
                list.Add(s!);
            }
        }
        return list;
    }

    /// <summary>Splits a comma-separated value into trimmed, non-empty entitlement strings.</summary>
    private static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }
        return value
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}
