using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Dumps gameplay tags for every equipment-class item so we can validate the
/// slot-role -> expected-tag mapping in InventorySlotViewModel.
/// </summary>
public class EquipmentTagsProbe
{
    private readonly ITestOutputHelper _output;
    public EquipmentTagsProbe(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Dump_TagsForEquipmentItems()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);

        // Cluster items by their id prefix to map roles -> real tag prefixes.
        string[] families = {
            "armor_chest", "armor_helmet", "armor_legs", "armor_arms",
            "backpack", "headlamp", "trinket", "watch", "shield",
            "weapon_", "knife_", "magnum_", "shotgun_", "club_",
            "key_", "ammo_", "food_", "drink_",
        };

        var seenTags = new Dictionary<string, HashSet<string>>();

        foreach (var fam in families)
        {
            var samples = catalog.Entries
                .Where(e => e.Id.StartsWith(fam, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();
            _output.WriteLine("");
            _output.WriteLine($"=== '{fam}*'  ({samples.Count} sample(s)) ===");
            foreach (var entry in samples)
            {
                _output.WriteLine($"  {entry.Id}  '{entry.DisplayName}'");
                _output.WriteLine($"    tags={string.Join(" | ", entry.Tags)}");
                foreach (var t in entry.Tags)
                {
                    var prefix = ExtractPrefix(t);
                    if (prefix is null) continue;
                    if (!seenTags.TryGetValue(fam, out var set)) seenTags[fam] = set = new();
                    set.Add(prefix);
                }
            }
        }

        _output.WriteLine("");
        _output.WriteLine("=== FAMILY → DISTINCT TAG PREFIXES ===");
        foreach (var (fam, set) in seenTags.OrderBy(kv => kv.Key))
        {
            _output.WriteLine($"  {fam,-16} → {string.Join(", ", set.OrderBy(s => s))}");
        }
    }

    private static string? ExtractPrefix(string rawTag)
    {
        // Tags may be rendered as "Item.Gear.HideTorso (FGameplayTagContainer)" by our
        // catalog's permissive parser; strip the trailing type annotation.
        var t = rawTag;
        var paren = t.IndexOf(' ');
        if (paren > 0) t = t[..paren];
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
