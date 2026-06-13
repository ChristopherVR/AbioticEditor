using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>PortalMap</c> (region saves): each entry is a world teleporter actor
/// (<c>BP_Teleporter_ParentBP_C_*</c>, the fixed teleporter pads found on the ground across
/// maps) whose persisted state is stored in a <c>SaveData_PortalStruct</c>. In every save
/// inspected the struct carries exactly two leaves:
/// <list type="bullet">
///   <item><c>ActorPath_</c> (<c>SoftObjectPath</c>) - the actor's own path; read-only and
///   already encoded in the entry key, so it is not surfaced as a field.</item>
///   <item><c>PortalActive_</c> (<c>BoolProperty</c>) - whether this teleporter pad has been
///   activated/unlocked. This is the one editable value, exposed here as <c>active</c>.</item>
/// </list>
///
/// <para><b>On the teleporter "tag"/linking the user asked about:</b> the linking that pairs
/// teleporters is <i>not</i> stored in the PortalMap struct - there is no tag, channel,
/// frequency, or group leaf inside <c>SaveData_PortalStruct</c> (verified across the Facility,
/// Salem, Train and Cabin region saves: only <c>ActorPath_</c> + <c>PortalActive_</c> are ever
/// present). World teleporter pads are static blueprints whose destinations are baked into the
/// level, so the save only persists their activated state. Player-placed teleporter networking
/// lives in <c>DeployedObjectMap</c> instead, encoded in the deployable's <c>PlayerMadeString_</c>
/// (the <c>}|!|{</c>-separated name format, same scheme documented for benches/personal
/// teleporters), with an (empty in observed saves) <c>GameplayTags_</c>
/// <c>GameplayTagContainer</c> on the same deployable. No discoverable built-in catalogue of
/// allowed teleporter tags exists in the saves or modeled tables, so a constrained
/// <c>WorldMapField.Choice</c> "tag" field is intentionally <b>not</b> exposed here; wiring it
/// would belong on a DeployedObjectMap feature once a sourced tag vocabulary is found.</para>
///
/// <para>Implementation mirrors <see cref="ElevatorMapFeature"/>: derive from
/// <see cref="WorldMapFeatureBase"/>, expose one typed bool field, and patch the single leaf in
/// <see cref="ApplyField"/> via <see cref="WorldMapAccessor"/>.</para>
/// </summary>
public sealed class PortalMapFeature : WorldMapFeatureBase
{
    /// <summary>Save leaf prefix (the blueprint hash suffix is matched by <c>FindByPrefix</c>).</summary>
    private const string PortalActivePrefix = "PortalActive_";

    public override string Id => "portals";

    public override string MapName => "PortalMap";

    public override string DisplayName => "Portals (Teleporters)";

    public override string Description =>
        "Set whether each fixed world teleporter pad is active (unlocked/usable).";

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var active = props.TryGetBool(PortalActivePrefix) ?? false;
        return new[]
        {
            WorldMapField.Bool("active", "Active", active,
                hint: "true = teleporter pad activated/usable, false = inactive"),
        };
    }

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        if (!string.Equals(fieldId, "active", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"unknown field '{fieldId}' (expected: active).");
        }
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = props.TryGetBool(PortalActivePrefix) ?? false;
        if (current == wanted)
        {
            return WorldEditResult.NoChange;
        }
        return WorldMapAccessor.SetBool(props, PortalActivePrefix, wanted)
            ? WorldEditResult.Success
            : WorldEditResult.Failure("the PortalActive field is missing from this teleporter entry.");
    }
}
