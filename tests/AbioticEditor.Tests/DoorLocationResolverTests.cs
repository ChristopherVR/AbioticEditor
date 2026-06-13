using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="DoorLocationResolver"/>: door world positions read on demand from
/// the cooked sub-level packages (the save stores state only, no placement).
/// </summary>
public class DoorLocationResolverTests
{
    [Fact]
    public void Resolves_positions_for_fixture_doors()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;
        Assert.NotNull(Fixtures.ServerWorldsDir);

        // Real doors from the live world save, grouped per sub-level.
        var facility = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");
        var doors = WorldSaveReader.ReadFromFile(facility).Doors;
        Assert.NotEmpty(doors);

        var byMap = doors
            .Select(d => DoorIdParser.Parse(d.Id))
            .Where(p => p.Actor.Length > 0)
            .GroupBy(p => p.Map, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .First();

        var locations = DoorLocationResolver.ForMap(provider, byMap.Key);
        Assert.NotEmpty(locations);

        var resolved = byMap.Count(p => locations.ContainsKey(p.Actor));
        Assert.True(resolved > byMap.Count() / 2,
            $"expected most of {byMap.Count()} doors in '{byMap.Key}' to resolve, got {resolved}");

        // Positions must be real world coordinates, not a wall of zeros.
        var any = byMap.Select(p => DoorLocationResolver.Resolve(provider, byMap.Key, p.Actor))
            .First(l => l is not null)!;
        Assert.True(Math.Abs(any.X) + Math.Abs(any.Y) + Math.Abs(any.Z) > 1,
            $"suspicious origin position: ({any.X}, {any.Y}, {any.Z})");
    }

    [Fact]
    public void Unknown_map_returns_empty_not_throws()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        Assert.Empty(DoorLocationResolver.ForMap(provider, "Facility_DoesNotExist_99"));
        Assert.Null(DoorLocationResolver.Resolve(provider, "Facility_DoesNotExist_99", "SimpleDoor_ParentBP_C_0"));
    }
}
