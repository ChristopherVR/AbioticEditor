using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for player-placed <b>Teleporter Pads</b> (<c>Deployed_TeleporterPad_C</c> entries in
/// <c>DeployedObjectMap</c>). A pad's "tag" - the identifier that links it into a teleporter
/// network with every other pad sharing the same tag - is stored as an integer
/// <c>TeleporterFrequency</c> inside the deployable's <c>ChangableData_ → DynamicProperties_</c>
/// array. This feature exposes that as a constrained <c>tag</c> choice (the 134 built-in tags
/// from <see cref="TeleporterTagCatalog"/>) plus the raw <c>frequency</c> integer for exact
/// control. Two pads set to the same tag link together.
///
/// <para>Implemented directly against <see cref="IWorldMapFeature"/> (not the simple
/// <see cref="WorldMapFeatureBase"/>) because it filters <c>DeployedObjectMap</c> to just the pad
/// class and reaches into a nested dynamic-property array rather than top-level leaves.</para>
/// </summary>
public sealed class TeleporterPadFeature : IWorldMapFeature
{
    private const string PadClassMarker = "TeleporterPad";
    private const string FrequencyKeyMarker = "TeleporterFrequency";

    public string Id => "teleporter-pads";

    public string MapName => "DeployedObjectMap";

    public string DisplayName => "Teleporter Pads";

    public string Description =>
        "Set each placed Teleporter Pad's tag (the link identifier); pads sharing a tag form a network.";

    public bool AppliesTo(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (!WorldMapAccessor.HasMap(save, MapName))
        {
            return false;
        }
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            if (IsPad(entry.Props))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Teleporter pads are placed deployables; removing one here is unsafe, so it's off.</summary>
    public bool SupportsRemoval => false;

    /// <inheritdoc/>
    public WorldEditResult Remove(SaveGame save, string entryKey)
        => WorldEditResult.Failure("Teleporter pads can't be removed here (pick them up in-game).");

    public IReadOnlyList<WorldMapEntry> Read(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        // First pass: collect every pad with its frequency so each pad can show which other
        // pads it links to (same non-zero frequency = same teleporter network).
        var pads = new List<(string Key, int Freq)>();
        foreach (var entry in WorldMapAccessor.Entries(save, MapName))
        {
            if (IsPad(entry.Props))
            {
                pads.Add((entry.Key, GetFrequency(entry.Props) ?? 0));
            }
        }

        var result = new List<WorldMapEntry>(pads.Count);
        for (var i = 0; i < pads.Count; i++)
        {
            var (key, freq) = pads[i];
            var linked = LinkSummary(pads, i, freq);
            result.Add(new WorldMapEntry(key, LabelFor(i + 1, freq), BuildFields(freq, linked)));
        }
        return result;
    }

    /// <summary>Human description of which other pads share this pad's tag (its link network).</summary>
    private static string LinkSummary(List<(string Key, int Freq)> pads, int index, int freq)
    {
        if (freq == 0)
        {
            return "(unassigned - not linked to any pad)";
        }
        var others = new List<string>();
        for (var j = 0; j < pads.Count; j++)
        {
            if (j != index && pads[j].Freq == freq)
            {
                others.Add($"Pad {j + 1}");
            }
        }
        return others.Count == 0
            ? "(only this pad uses this tag - set another pad to the same tag to link them)"
            : string.Join(", ", others);
    }

    public WorldEditResult SetField(SaveGame save, string entryKey, string fieldId, string? value)
    {
        ArgumentNullException.ThrowIfNull(save);
        var props = WorldMapAccessor.FindEntry(save, MapName, entryKey);
        if (props is null || !IsPad(props))
        {
            return WorldEditResult.Failure($"no teleporter pad '{entryKey}' in this save.");
        }

        int target;
        switch (fieldId)
        {
            case "tag":
                var freq = TeleporterTagCatalog.Frequency(value);
                if (freq is null)
                {
                    return WorldEditResult.Failure(
                        $"'{value}' is not a known teleporter tag. Pick one of the {TeleporterTagCatalog.Choices.Count} built-in tags.");
                }
                target = freq.Value;
                break;
            case "frequency":
                if (!WorldMapAccessor.TryParseInt(value, out target))
                {
                    return WorldEditResult.Failure($"'{value}' is not a valid integer.");
                }
                // Forward-compat: don't cap at the known max - a future DLC may add tags beyond
                // 133. Only a negative value is invalid (0 = unassigned).
                if (target < 0)
                {
                    return WorldEditResult.Failure("frequency cannot be negative (0 = unassigned).");
                }
                break;
            default:
                return WorldEditResult.Failure($"unknown field '{fieldId}' (expected: tag, frequency).");
        }

        var current = GetFrequency(props);
        if (current == target)
        {
            return WorldEditResult.NoChange;
        }
        return SetFrequency(props, target)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("this pad has no TeleporterFrequency property to set.");
    }

    private static WorldMapField[] BuildFields(int frequency, string linkedSummary)
    {
        var freq = frequency;
        var tagHint = TeleporterTagCatalog.IsKnown(freq)
            ? "Pads sharing a tag link together; '(none)' is unassigned."
            : "This pad uses a tag this build has no name for (a newer game/DLC version?); "
                + "it's shown by number and preserved. Pick a known tag or edit the raw frequency.";
        return new[]
        {
            WorldMapField.Choice("tag", "Tag", TeleporterTagCatalog.Label(freq),
                TeleporterTagCatalog.ChoicesFor(freq), hint: tagHint),
            WorldMapField.Integer("frequency", "Frequency (raw)", freq,
                hint: $"The raw tag index (0 = unassigned, 1..{TeleporterTagCatalog.MaxFrequency} known; "
                    + "higher values from a future update are allowed and shown as Unknown)."),
            WorldMapField.ReadOnly("linkedWith", "Linked pads", linkedSummary,
                hint: "Pads sharing this pad's tag teleport to each other. Give two pads the same Tag to link them."),
        };
    }

    private static string LabelFor(int ordinal, int frequency)
        => $"Teleporter Pad {ordinal}: {TeleporterTagCatalog.Label(frequency)}";

    /// <summary>True when a deployable entry's class is the teleporter pad blueprint.</summary>
    private static bool IsPad(IList<FPropertyTag> deployableProps)
    {
        var classValue = deployableProps.FindByPrefix("Class_")?.Property?.Value;
        var name = classValue switch
        {
            SoftObjectPath sop => sop.AssetName?.Value ?? sop.ToString(),
            null => null,
            var v => v.ToString(),
        };
        return name is not null && name.Contains(PadClassMarker, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- ChangableData_ → DynamicProperties_ → TeleporterFrequency ----------

    private static IList<FPropertyTag>? DynamicPropertyEntry(IList<FPropertyTag> deployableProps)
    {
        // Note the in-save spelling "ChangableData_" (sic).
        if (deployableProps.FindByPrefix("ChangableData_")?.Property is not StructProperty cd
            || cd.Value is not PropertiesStruct cps)
        {
            return null;
        }
        if (cps.Properties.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty arr
            || arr.Value is null)
        {
            return null;
        }
        for (var i = 0; i < arr.Value.Length; i++)
        {
            if (arr.Value.GetValue(i) is not StructProperty sp || sp.Value is not PropertiesStruct ps)
            {
                continue;
            }
            var key = ps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is not null && key.Contains(FrequencyKeyMarker, StringComparison.OrdinalIgnoreCase))
            {
                return ps.Properties;
            }
        }
        return null;
    }

    private static int? GetFrequency(IList<FPropertyTag> deployableProps)
    {
        var entry = DynamicPropertyEntry(deployableProps);
        return entry?.FindByPrefix("Value")?.Property?.Value is int v ? v : null;
    }

    private static bool SetFrequency(IList<FPropertyTag> deployableProps, int frequency)
    {
        var entry = DynamicPropertyEntry(deployableProps);
        if (entry?.FindByPrefix("Value")?.Property is not { } valueProp)
        {
            return false;
        }
        valueProp.Value = frequency;
        return true;
    }
}
