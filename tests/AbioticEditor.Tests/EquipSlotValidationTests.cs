using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Enum-first equipment slot validation (<see cref="EquipSlotTypes"/>): every item row
/// carries its E_InventorySlotType in EquipmentData_ -> EquipSlot_, and a slot accepts an
/// item iff that value matches the role's expected type (or is the EquipmentSlots_All
/// wildcard, 2). Fixes "legs in head slot" / trinkets in transmog. Expectations per
/// dotnet/docs/research-slot-types.md.
/// </summary>
public class EquipSlotValidationTests
{
    private readonly ITestOutputHelper _output;
    public EquipSlotValidationTests(ITestOutputHelper output) { _output = output; }

    private static ItemCatalogEntry Entry(int equipSlot, string id = "test_item")
        => new(id, id, null, null, 1, 0, false, 0, Array.Empty<string>(), 0, equipSlot);

    // -------------------------------------------------- pure mapping rules

    [Theory]
    [InlineData("HEAD", 5)]
    [InlineData("LEGS", 6)]
    [InlineData("BACK", 7)]
    [InlineData("ARMS", 12)]
    [InlineData("SUIT", 13)]
    [InlineData("CHEST", 14)]
    [InlineData("HEADLAMP", 15)]
    [InlineData("TRINKET", 16)]
    [InlineData("WATCH", 17)]
    [InlineData("HACKER", 18)]
    [InlineData("SHIELD", 19)]
    [InlineData("PET", 21)]
    public void ExpectedFor_MapsEveryRole(string role, int expected)
    {
        Assert.Equal(expected, EquipSlotTypes.ExpectedFor(role));

        // Exact match and the EquipmentSlots_All wildcard are accepted; everything
        // else is rejected.
        Assert.Null(EquipSlotTypes.ValidateForRole(role, Entry(expected)));
        Assert.Null(EquipSlotTypes.ValidateForRole(role, Entry(EquipSlotTypes.All)));
        Assert.True(EquipSlotTypes.Accepts(Entry(expected), role));
        Assert.True(EquipSlotTypes.Accepts(Entry(EquipSlotTypes.All), role));
    }

    [Fact]
    public void NullRoleOrEntry_NeverWarns()
    {
        Assert.Null(EquipSlotTypes.ValidateForRole(null, Entry(5)));
        Assert.Null(EquipSlotTypes.ValidateForRole("HEAD", null));
        Assert.Null(EquipSlotTypes.ValidateForRole(null, null));
    }

    [Fact]
    public void RejectionMessages_NameTheActualSlot()
    {
        // A legs item in the head slot names EquipmentSlot_Legs.
        var msg = EquipSlotTypes.ValidateForRole("HEAD", Entry(6));
        Assert.NotNull(msg);
        Assert.Contains("EquipmentSlot_Legs", msg);
        Assert.Contains("HEAD", msg);

        // Non-equippable values (0 Hotbar / 1 InventoryBackpack) say "not equippable".
        Assert.Contains("Not equippable", EquipSlotTypes.ValidateForRole("HEAD", Entry(0))!);
        Assert.Contains("Not equippable", EquipSlotTypes.ValidateForRole("TRINKET", Entry(1))!);
    }

    // -------------------------------------------- catalog-driven (real rows)

    private static readonly string[] AllRoles =
    {
        "HEAD", "LEGS", "BACK", "ARMS", "SUIT", "CHEST",
        "HEADLAMP", "TRINKET", "WATCH", "HACKER", "SHIELD", "PET",
    };

    [Fact]
    public void RealItems_ValidatePerTheirEquipSlotColumn()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);

        // (item id, role, fits) - per the probe results in research-slot-types.md.
        var cases = new (string Id, string Role, bool Fits)[]
        {
            ("armor_helmet_cqc", "HEAD", true),
            ("armor_legs_makeshift", "HEAD", false),
            ("trinket_kylie", "TRINKET", true),
            ("trinket_kylie", "CHEST", false),
            ("keypad_hacker", "HACKER", true),
            ("suit_hazmat", "SUIT", true),
            // suit_hazmat_casual really is a LEGS item (EquipSlot 6) - validation must
            // be per-row, never by id prefix.
            ("suit_hazmat_casual", "LEGS", true),
            ("suit_hazmat_casual", "SUIT", false),
        };

        foreach (var (id, role, fits) in cases)
        {
            var entry = catalog.Find(id);
            Assert.NotNull(entry);
            var problem = EquipSlotTypes.ValidateForRole(role, entry);
            _output.WriteLine($"{id} (EquipSlot={entry!.EquipSlot}) in {role}: {(problem ?? "ok")}");
            if (fits)
            {
                Assert.Null(problem);
            }
            else
            {
                Assert.NotNull(problem);
            }
        }

        // A non-equipment item (EquipSlot 1) is rejected by EVERY equipment slot.
        var bandage = catalog.Find("bandage");
        Assert.NotNull(bandage);
        _output.WriteLine($"bandage EquipSlot={bandage!.EquipSlot}");
        foreach (var role in AllRoles)
        {
            Assert.NotNull(EquipSlotTypes.ValidateForRole(role, bandage));
        }
    }

    /// <summary>All five keypad hacker tiers exist and are HACKER-slot items (item 4's chain ids).</summary>
    [Fact]
    public void KeypadHackerTierChain_AllFiveTiersExist()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);
        string[] tiers =
        {
            "keypad_hacker", "keypad_hacker_t2", "keypad_hacker_t3",
            "keypad_hacker_t4", "keypad_hacker_t5",
        };
        foreach (var id in tiers)
        {
            var entry = catalog.Find(id);
            Assert.NotNull(entry);
            Assert.Equal(18, entry!.EquipSlot); // EquipmentSlot_Hacker
            _output.WriteLine($"{id}: '{entry.DisplayName}' EquipSlot={entry.EquipSlot}");
        }
    }
}
