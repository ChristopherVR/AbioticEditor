using System.Text;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Turns a resource-node actor key/class into a friendly harvestable type name and a stable type
/// token. Node keys look like
/// <c>/Game/Maps/Facility.Facility:PersistentLevel.Resource_Micronode_LeyakEssence_C_2147263889</c>;
/// stripping the <c>Resource[_Micronode]/ResourceNode</c> prefix and the trailing
/// <c>_C[_number]</c> blueprint suffix, then splitting camel case, yields "Leyak Essence". Used for
/// the node list label and for grouping/icon lookup.
/// </summary>
public static class ResourceNodeNaming
{
    private static readonly string[] Prefixes =
    {
        "Resource_Micronode_", "Resource_MicroNode_", "ResourceNode_", "Resource_",
        "Pickup_", "Destructible_", "Deployed_",
    };

    /// <summary>The stable bare type token (e.g. <c>LeyakEssence</c>) parsed from a node key.</summary>
    public static string TypeToken(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        // Trim to the actor name (after the last '.').
        var dot = key.LastIndexOf('.');
        var name = dot >= 0 && dot < key.Length - 1 ? key[(dot + 1)..] : key;

        // Drop a leading known prefix (first match).
        foreach (var prefix in Prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        // Drop the trailing blueprint suffix: "_C", "_C_<digits>", or a bare "_<digits>".
        name = StripBlueprintSuffix(name);
        return name.Length == 0 ? (dot >= 0 ? key[(dot + 1)..] : key) : name;
    }

    /// <summary>A readable type name (e.g. "Leyak Essence") for a node key.</summary>
    public static string FriendlyType(string key)
    {
        var token = TypeToken(key);
        return SplitCamelCase(token.Replace('_', ' ')).Trim();
    }

    private static string StripBlueprintSuffix(string name)
    {
        // "_C_2147263889" or "_C"
        var cIdx = name.LastIndexOf("_C", StringComparison.Ordinal);
        if (cIdx > 0 && (cIdx == name.Length - 2 || IsAllDigits(name[(cIdx + 2)..].TrimStart('_'))))
        {
            return name[..cIdx];
        }
        // A bare trailing "_<digits>".
        var us = name.LastIndexOf('_');
        if (us > 0 && IsAllDigits(name[(us + 1)..]))
        {
            return name[..us];
        }
        return name;
    }

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }
        foreach (var c in s)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static string SplitCamelCase(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        var sb = new StringBuilder(text.Length + 6);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(text[i - 1]) || (i + 1 < text.Length && char.IsLower(text[i + 1]))))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
