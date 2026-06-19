using AbioticEditor.Core.Saves;
using AbioticEditor.Core.Steam;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>ServerEntitlements</c> (the metadata save): the per-player ownership
/// entitlements list, keyed by a player's SteamID64. Each entry value is a
/// <c>StructProperty -&gt; PropertiesStruct</c> with one member, <c>Entitlements</c> (an
/// <see cref="ArrayProperty"/> of strings) - e.g. <c>EarlyAccess</c>, <c>SupportersEdition</c>.
///
/// <para>Rather than a single comma-separated text blob, every <b>known</b> entitlement is shown
/// as its own on/off toggle so the player can see all available grants at a glance; any entitlement
/// already present that this build doesn't recognise is shown as an extra toggle too, so nothing is
/// hidden or lost. The SteamID key is resolved to the player's Steam persona name where known.</para>
/// </summary>
public sealed class ServerEntitlementsFeature : WorldMapFeatureBase
{
    /// <summary>Save leaf name. The member has no blueprint hash suffix, so this is exact.</summary>
    private const string EntitlementsPrefix = "Entitlements";

    /// <summary>The entitlements the game grants, with friendly labels (extend as more are seen).</summary>
    private static readonly (string Id, string Label)[] Known =
    {
        ("EarlyAccess", "Early Access"),
        ("SupportersEdition", "Supporter's Edition"),
    };

    // Steam persona names (owner id -> name) from the local machine, loaded once. Empty when the
    // accounts file isn't present (e.g. a headless dedicated server), in which case keys show bare.
    private static IReadOnlyDictionary<string, string>? _personaCache;

    public override string Id => "server-entitlements";

    public override string MapName => "ServerEntitlements";

    public override string DisplayName => "Server Entitlements";

    public override string Description =>
        "Per-player ownership entitlements (Early Access, Supporter's Edition), keyed by SteamID64. "
        + "Toggle an entitlement on or off for each player.";

    /// <summary>Entitlement entries are server metadata, not removable level actors.</summary>
    public override bool SupportsRemoval => false;

    /// <summary>Show the player's Steam persona name when known, with the SteamID for reference.</summary>
    protected override string LabelFor(string key, IList<FPropertyTag> props)
    {
        if (Persona.TryGetValue(key, out var name) && name.Length > 0)
        {
            return $"{name} ({key})";
        }
        return key;
    }

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var held = new HashSet<string>(ReadEntitlements(props), StringComparer.OrdinalIgnoreCase);
        var fields = new List<WorldMapField>();

        // One toggle per known entitlement, so all available grants are visible.
        foreach (var (id, label) in Known)
        {
            fields.Add(WorldMapField.Bool(id, label, held.Contains(id),
                hint: $"Whether this player holds the {label} entitlement."));
        }

        // Surface (and preserve) any entitlement this build doesn't know about as its own toggle.
        foreach (var extra in held.Where(h => !Known.Any(k => string.Equals(k.Id, h, StringComparison.OrdinalIgnoreCase))))
        {
            fields.Add(WorldMapField.Bool(extra, extra, value: true,
                hint: "An entitlement this editor doesn't have a friendly name for; left as-is unless you turn it off."));
        }

        // Free-text add field, so a future/unknown entitlement can be granted without a code change.
        fields.Add(new WorldMapField(AddFieldId, "Add entitlement", string.Empty, WorldFieldKind.Text,
            Editable: true, Options: null,
            Hint: "Type a new entitlement id and commit to grant it (appears as its own toggle after "
                + "the next save/reload). For entitlements this build doesn't know about yet."));

        return fields;
    }

    /// <summary>Field id of the free-text "add a new entitlement" input.</summary>
    private const string AddFieldId = "addEntitlement";

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (props.FindByPrefix(EntitlementsPrefix)?.Property is not ArrayProperty arr)
        {
            return WorldEditResult.Failure("the Entitlements array is missing from this entry.");
        }

        // The free-text add field takes an entitlement id (not a bool): grant it if new.
        if (string.Equals(fieldId, AddFieldId, StringComparison.OrdinalIgnoreCase))
        {
            var id = value?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                return WorldEditResult.NoChange;
            }
            var existing = ReadEntitlements(props);
            if (existing.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                return WorldEditResult.NoChange;
            }
            existing.Add(id);
            arr.Value = existing.Select(s => new FString(s)).ToArray();
            return WorldEditResult.Success;
        }

        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = ReadEntitlements(props);
        var has = current.Contains(fieldId, StringComparer.OrdinalIgnoreCase);
        if (has == wanted)
        {
            return WorldEditResult.NoChange;
        }

        if (wanted)
        {
            current.Add(fieldId);
        }
        else
        {
            current.RemoveAll(s => string.Equals(s, fieldId, StringComparison.OrdinalIgnoreCase));
        }

        arr.Value = current.Select(s => new FString(s)).ToArray();
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

    private static IReadOnlyDictionary<string, string> Persona
    {
        get
        {
            if (_personaCache is not null)
            {
                return _personaCache;
            }
            try
            {
                _personaCache = SteamPersonaIndex.LoadMachineAccounts();
            }
            catch
            {
                _personaCache = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            return _personaCache;
        }
    }
}
