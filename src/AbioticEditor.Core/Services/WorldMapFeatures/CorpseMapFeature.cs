using System.Text.RegularExpressions;
using AbioticEditor.Core.Saves;
using UeSaveGame;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>CorpseMap</c> (region saves): each entry is a dead NPC's corpse actor
/// (<c>CharacterCorpse_&lt;NpcClass&gt;_C_*</c>), confirmed across the server fixture and a live
/// server backup (Facility, Dam/Dam_Hydroplant/Dam_Lower/Dam_Waterfall, DarkFusion, DF_Labs,
/// DF_RadWaste, Labs_Adjustment, MFHQ, MFWest). The value is a
/// <c>StructProperty -&gt; PropertiesStruct</c> with exactly three leaves in every entry seen:
/// <list type="bullet">
///   <item><c>ActorPath_</c> (<c>SoftObjectPath</c>) - the corpse actor's own path; read-only and
///   already encoded in the entry key, so it is not surfaced as a field.</item>
///   <item><c>IsGibbed_</c> (<c>BoolProperty</c>) - whether the corpse was gibbed (destroyed
///   rather than left as a body). Read-only context, shown for reference.</item>
///   <item><c>IsLooted_</c> (<c>BoolProperty</c>) - whether the player has already looted this
///   corpse. Read-only context, shown for reference.</item>
/// </list>
///
/// <para>There is no in-game reason to flip <c>IsGibbed</c>/<c>IsLooted</c> by hand, so neither
/// is made editable; the useful edit is clearing clutter, which this feature exposes as
/// per-entry removal (<see cref="WorldMapFeatureBase.Remove"/> via <c>SupportsRemoval</c>,
/// the default). The row label is the NPC class parsed out of the actor name (e.g.
/// <c>CharacterCorpse_Human_BP_C_4</c> -&gt; "Human Corpse", <c>CharacterCorpse_OrderGrunt_C_1</c>
/// -&gt; "Order Grunt Corpse") since the raw actor path carries no other identifying name.</para>
/// </summary>
public sealed class CorpseMapFeature : WorldMapFeatureBase
{
    private const string ActorClassPrefix = "CharacterCorpse_";
    private static readonly Regex BlueprintSuffix = new(@"_C(_\d+)?$", RegexOptions.Compiled);
    private static readonly Regex BpMarker = new("_BP$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CamelBoundary = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    private const string IsGibbedPrefix = "IsGibbed_";
    private const string IsLootedPrefix = "IsLooted_";

    public override string Id => "corpses";

    public override string MapName => "CorpseMap";

    public override string DisplayName => "Corpses";

    public override string Description =>
        "NPC corpses left in the region. Remove one to clear the clutter (and any loot still on it).";

    /// <summary>Corpse removal is the whole point of this tab, so it stays the default "on".</summary>
    public override bool SupportsRemoval => true;

    public override string RemoveActionLabel => "Remove this Corpse";

    protected override string LabelFor(string key, IList<FPropertyTag> props)
    {
        var name = ShortLabel(key);
        if (name.StartsWith(ActorClassPrefix, StringComparison.Ordinal))
        {
            name = name[ActorClassPrefix.Length..];
        }
        name = BlueprintSuffix.Replace(name, string.Empty);
        name = BpMarker.Replace(name, string.Empty);
        name = name.Replace('_', ' ').Trim();
        name = CamelBoundary.Replace(name, " ");
        return name.Length == 0 ? $"{ShortLabel(key)} Corpse" : $"{name} Corpse";
    }

    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        var gibbed = props.TryGetBool(IsGibbedPrefix) ?? false;
        var looted = props.TryGetBool(IsLootedPrefix) ?? false;
        return new[]
        {
            WorldMapField.ReadOnly("gibbed", "Gibbed", gibbed ? "Yes" : "No",
                hint: "whether this corpse was gibbed rather than left as a body"),
            WorldMapField.ReadOnly("looted", "Looted", looted ? "Yes" : "No",
                hint: "whether the player has already looted this corpse"),
        };
    }

    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
        => WorldEditResult.Failure(
            $"unknown field '{fieldId}': Corpses has no editable fields, remove the entry instead.");
}
