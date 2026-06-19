using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Coverage for the customization/transmog/respawn data layer:
/// transmog slots + visibility in player saves, the read-only respawn model,
/// bed-claim parsing on deployables, the ScientistCustomization file and the
/// DT_Customization_* catalog. Schema documented in docs/research-customization.md.
/// </summary>
public class CustomizationModelTests
{
    private readonly ITestOutputHelper _output;

    public CustomizationModelTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FixturePlayerSave(string steamId = "76561198128277890")
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", $"Player_{steamId}.sav");
        return File.Exists(path) ? path : null;
    }

    // ------------------------------------------------------------- transmog

    [Fact]
    public void Reads_TransmogSlotsAndVisibility()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);

        Assert.Equal(6, data.TransmogSlots.Count);
        Assert.Equal("armor_helmet_cowl", data.TransmogSlots[1].ItemId);
        Assert.Equal(12, data.TransmogVisibility.Count);

        foreach (var slot in data.TransmogSlots)
        {
            _output.WriteLine($"  transmog[{slot.Index}] = {(slot.IsEmpty ? "(empty)" : slot.ItemId)}");
        }
        _output.WriteLine($"  visibility = [{string.Join(", ", data.TransmogVisibility)}]");
    }

    [Fact]
    public void TransmogEdit_RoundTripsThroughFile()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-transmog-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(path!, tempPath);

            var data = PlayerSaveReader.ReadFromFile(tempPath);
            var originalSkillXp = data.Skills.Select(s => s.Xp).ToList();
            var originalEquipment0 = data.Inventory.Equipment[0].ItemId;
            var originalPropertyCount = data.Raw.Properties!.Count;

            // Swap the cowl transmog for a different cosmetic and hide slot 0.
            var updatedSlots = data.TransmogSlots
                .Select(s => s.Index == 1 ? s with { ItemId = "armor_helmet_security" } : s)
                .ToList();
            PlayerSaveWriter.ApplyTransmogSlots(data, updatedSlots);

            var updatedVisibility = data.TransmogVisibility.ToArray();
            updatedVisibility[0] = !updatedVisibility[0];
            PlayerSaveWriter.ApplyTransmogVisibility(data, updatedVisibility);

            PlayerSaveWriter.WriteToFile(data, tempPath);

            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.Equal("armor_helmet_security", reloaded.TransmogSlots[1].ItemId);
            Assert.Equal(updatedVisibility[0], reloaded.TransmogVisibility[0]);
            Assert.Equal(6, reloaded.TransmogSlots.Count);
            Assert.Equal(12, reloaded.TransmogVisibility.Count);

            // Untouched regions survive the rewrite.
            Assert.Equal(originalPropertyCount, reloaded.Raw.Properties!.Count);
            Assert.Equal(originalSkillXp, reloaded.Skills.Select(s => s.Xp).ToList());
            Assert.Equal(originalEquipment0, reloaded.Inventory.Equipment[0].ItemId);
            Assert.Equal(data.TransmogSlots[0].ItemId, reloaded.TransmogSlots[0].ItemId);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void ApplyTransmog_SkipsSavesWithoutTheArrays()
    {
        // A save missing TransmogInventory_/TransmogVisibility_ must be skipped silently.
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        var root = (UeSaveGame.StructData.PropertiesStruct)((UeSaveGame.PropertyTypes.StructProperty)data.Raw
            .Properties!.First(t => t.Name.Value.StartsWith("CharacterSaveData", StringComparison.Ordinal)).Property!).Value!;
        root.Properties = root.Properties
            .Where(t => !t.Name.Value.StartsWith("TransmogInventory_", StringComparison.Ordinal)
                        && !t.Name.Value.StartsWith("TransmogVisibility_", StringComparison.Ordinal))
            .ToList();

        // Must not throw.
        PlayerSaveWriter.ApplyTransmogSlots(data, data.TransmogSlots);
        PlayerSaveWriter.ApplyTransmogVisibility(data, data.TransmogVisibility);
    }

    // -------------------------------------------------------------- respawn

    [Fact]
    public void Reads_RespawnFields()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        foreach (var playerSave in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            var data = PlayerSaveReader.ReadFromFile(playerSave);
            _output.WriteLine(
                $"{Path.GetFileName(playerSave)}: ({data.RespawnX:F1}, {data.RespawnY:F1}, {data.RespawnZ:F1}) " +
                $"level={data.RespawnLevelGuid} terminal={data.TerminalRespawnId}");

            // All four fixture players respawn in WorldSave_Facility_Office1's level.
            Assert.Equal("EB422B4245ACC9F546C26989FC936F5C", data.RespawnLevelGuid);
            Assert.False(string.IsNullOrEmpty(data.TerminalRespawnId));
            Assert.NotEqual(0, data.RespawnX);
        }

        // Tribbes' respawn point sits next to his claimed bed (research doc §3).
        var tribbes = PlayerSaveReader.ReadFromFile(
            Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav"));
        Assert.InRange(tribbes.RespawnX, -15754, -15752);
        Assert.InRange(tribbes.RespawnY, 11352, 11354);
        Assert.InRange(tribbes.RespawnZ, 107, 109);
    }

    // ----------------------------------------------------------- bed claims

    [Fact]
    public void BedClaims_ParseOwnerFromCustomText()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        Assert.True(File.Exists(path), $"missing fixture: {path}");

        var save = WorldSaveReader.ReadFromFile(path);
        var beds = save.Deployables.Where(d => d.IsBed).ToList();
        Assert.NotEmpty(beds);

        foreach (var bed in beds)
        {
            _output.WriteLine($"  {bed.Id} {bed.FriendlyClass}: owner={bed.OwnerSteamId} '{bed.OwnerName}' → {bed.DisplayName}");
        }

        var tribbes = beds.FirstOrDefault(b => b.OwnerName == "Tribbes");
        Assert.NotNull(tribbes);
        Assert.Equal(76561197993781479UL, tribbes!.OwnerSteamId);

        // Claim strings render as a claim, never as the raw separator soup.
        Assert.Contains("claimed by Tribbes", tribbes.DisplayName);
        Assert.DoesNotContain(WorldDeployable.ClaimSeparator, tribbes.DisplayName);

        // The fixture also has an unclaimed bed (bare separator).
        var unclaimed = beds.FirstOrDefault(b => b.HasClaimMarker && b.OwnerName is null);
        Assert.NotNull(unclaimed);
        Assert.Null(unclaimed!.OwnerSteamId);
        Assert.Contains("unclaimed", unclaimed.DisplayName);
    }

    [Fact]
    public void ParseClaim_HandlesAllForms()
    {
        Assert.Equal(((string?)"76561197993781479", (string?)"Tribbes"), WorldDeployable.ParseClaim("76561197993781479}|!|{Tribbes"));
        Assert.Equal(((string?)null, (string?)null), WorldDeployable.ParseClaim("}|!|{"));
        Assert.Equal(((string?)null, (string?)null), WorldDeployable.ParseClaim("My Fridge"));
        Assert.Equal(((string?)null, (string?)null), WorldDeployable.ParseClaim(null));
        Assert.Equal(((string?)null, (string?)null), WorldDeployable.ParseClaim(""));
        // Name without a parseable id still yields the name.
        Assert.Equal(((string?)null, "Bob"), WorldDeployable.ParseClaim("}|!|{Bob"));

        // A non-Steam owner id is opaque: OwnerId carries it, the numeric convenience is null.
        var nonSteam = WorldDeployable.ParseClaim("epic-9f8e}|!|{Bob");
        Assert.Equal(("epic-9f8e", "Bob"), nonSteam);
        var bed = new WorldDeployable("id", "Deployed_CraftedBed_C", 0, 0, 0, false, 0,
            CustomName: "epic-9f8e}|!|{Bob");
        Assert.Equal("epic-9f8e", bed.OwnerId);
        Assert.Null(bed.OwnerSteamId);
    }

    // -------------------------------------------------- customization catalog

    [Fact]
    public void CustomizationCatalog_LoadsAllTables()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = CustomizationCatalog.LoadFrom(provider);

        Assert.True(catalog.ContainsKey("DT_Customization_Head"), "DT_Customization_Head missing");
        var heads = catalog["DT_Customization_Head"];
        Assert.True(heads.Count >= 16, $"expected >= 16 head rows, got {heads.Count}");
        Assert.Contains(heads, h => h.RowName == "Head_M01a");

        foreach (var (table, options) in catalog.OrderBy(kv => kv.Key))
        {
            _output.WriteLine($"  {table}: {options.Count} row(s), e.g. {options[0].RowName} = '{options[0].DisplayName}'");
            Assert.All(options, o => Assert.False(string.IsNullOrEmpty(o.DisplayName)));
        }

        // One table per distinct KnownFields entry (13).
        Assert.Equal(
            CustomizationSaveFile.KnownFields.Select(f => f.TableName).Distinct().Count(),
            catalog.Count);
    }

    // ----------------------------------------------- ScientistCustomization

    private static string? FindLocalCustomizationFile()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticFactor", "Saved", "SaveGames");
        if (!Directory.Exists(root)) return null;
        return Directory
            .EnumerateFiles(root, "ScientistCustomization_*.sav", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    [Fact]
    public void CustomizationSaveFile_ParsesLocalFileWhenPresent()
    {
        // Only runs on machines that have actually played AF locally (LoadFor returns
        // null on CI - that's the supported "no local install" path).
        var file = FindLocalCustomizationFile();
        if (file is null)
        {
            _output.WriteLine("No local ScientistCustomization_*.sav; skipping.");
            return;
        }

        var save = CustomizationSaveFile.LoadFromFile(file);
        Assert.Equal(file, save.FilePath);
        Assert.NotEmpty(save.Fields);

        foreach (var f in save.Fields)
        {
            _output.WriteLine($"  {f.PropertyName} ({f.Label}, {f.TableName}) = {f.CurrentValue}");
            Assert.False(string.IsNullOrEmpty(f.CurrentValue));
        }

        Assert.Contains(save.Fields, f => f.Label == "Head");
        // The beard property is lowercase in the file; it must still be found.
        Assert.Contains(save.Fields,
            f => f.PropertyName.Equals("customization_beard", StringComparison.OrdinalIgnoreCase));

        // LoadFor/SlotsFor resolve the same file through the steamid64 folder name.
        var dirName = Path.GetFileName(Path.GetDirectoryName(file)!);
        if (ulong.TryParse(dirName, out var steamId))
        {
            var slots = CustomizationSaveFile.SlotsFor(steamId);
            Assert.NotEmpty(slots);
            var viaLoadFor = CustomizationSaveFile.LoadFor(steamId, slots[0]);
            Assert.NotNull(viaLoadFor);
            Assert.NotEmpty(viaLoadFor!.Fields);
        }
    }

    [Fact]
    public void CustomizationSaveFile_SaveRoundTripsOnTempCopy()
    {
        var file = FindLocalCustomizationFile();
        if (file is null)
        {
            _output.WriteLine("No local ScientistCustomization_*.sav; skipping.");
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-customization-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(file, tempPath);

            var save = CustomizationSaveFile.LoadFromFile(tempPath);
            var head = save.Fields.First(f => f.Label == "Head");
            var hair = save.Fields.First(f => f.Label == "Hair Style");
            var newHead = head.CurrentValue == "Head_M01a" ? "Head_F01a" : "Head_M01a";

            save.Save(new Dictionary<string, string> { [head.PropertyName] = newHead });

            Assert.True(File.Exists(tempPath + ".bak"), "expected a .bak backup next to the save");

            var reloaded = CustomizationSaveFile.LoadFromFile(tempPath);
            Assert.Equal(newHead, reloaded.Fields.First(f => f.Label == "Head").CurrentValue);
            // Untouched fields keep their values.
            Assert.Equal(hair.CurrentValue, reloaded.Fields.First(f => f.Label == "Hair Style").CurrentValue);
            Assert.Equal(save.Fields.Count, reloaded.Fields.Count);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }
}
