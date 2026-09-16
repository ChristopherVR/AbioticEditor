using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Deployable paint colour is delta-serialized the same way as everything else: it lives in a
/// <c>{Key:EDynamicProperty::PaintColor,Value:int}</c> entry inside the deployable's
/// <c>ChangableData_.DynamicProperties_</c> array (see <see cref="DeployablePaintCatalog"/> and
/// <c>docs/reference/research/research-deployable-paint.md</c>). These tests prove the writer can
/// add/change/clear that entry and that <see cref="WorldSaveReader"/> reads the result back.
/// </summary>
public sealed class DeployablePaintWriteTests
{
    [Fact]
    public void PaintColor_SetChangeAndClear_RoundTrips()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(fixture)) return;
        var temp = Path.Combine(Path.GetTempPath(), $"abf-paint-{Guid.NewGuid():N}.sav");

        try
        {
            File.Copy(fixture, temp);
            var data = WorldSaveReader.ReadFromFile(temp);

            // A deployable with a confirmed paint profile that isn't already painted, so the
            // first write proves the array-graft/append path, not just an in-place value change.
            var target = data.Deployables.First(d => d.SupportsPaint && d.PaintColorValue is null);

            Assert.True(WorldSaveWriter.ApplyDeployablePaintColor(data, target.Id, 5)); // Purple
            WorldSaveWriter.WriteToFile(data, temp);

            var reloaded = WorldSaveReader.ReadFromFile(temp);
            var reloadedDeployable = reloaded.Deployables.Single(d => d.Id == target.Id);
            Assert.Equal(5, reloadedDeployable.PaintColorValue);
            Assert.True(reloadedDeployable.IsPainted);
            Assert.Equal("Purple", reloadedDeployable.PaintColorName);

            // Change an already-painted entry in place (matches the SetOrAdd "in place" path).
            Assert.True(WorldSaveWriter.ApplyDeployablePaintColor(reloaded, target.Id, 8)); // Cyan
            WorldSaveWriter.WriteToFile(reloaded, temp);
            var repainted = WorldSaveReader.ReadFromFile(temp);
            Assert.Equal(8, repainted.Deployables.Single(d => d.Id == target.Id).PaintColorValue);

            // Clearing writes EPaintColor::None explicitly rather than removing the entry.
            Assert.True(WorldSaveWriter.ApplyDeployablePaintColor(repainted, target.Id, null));
            WorldSaveWriter.WriteToFile(repainted, temp);
            var cleared = WorldSaveReader.ReadFromFile(temp);
            var clearedDeployable = cleared.Deployables.Single(d => d.Id == target.Id);
            Assert.False(clearedDeployable.IsPainted);
            Assert.Null(clearedDeployable.PaintColorName);
        }
        finally
        {
            DeleteWithBackup(temp);
        }
    }

    [Fact]
    public void PaintColor_AlreadyPaintedFixtureEntries_ReadKnownValues()
    {
        // Confirmed against the real fixture (see the research note): several deployables in
        // this world are already painted, read straight off DynamicProperties_ with no edit.
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(fixture)) return;

        var data = WorldSaveReader.ReadFromFile(fixture);
        var painted = data.Deployables.Where(d => d.IsPainted).ToList();
        Assert.NotEmpty(painted);
        Assert.All(painted, d => Assert.InRange(d.PaintColorValue!.Value, 0, 13));
        Assert.Contains(painted, d => d.ClassName == "Deployed_CraftingBench_Default_C");
    }

    private static void DeleteWithBackup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
    }
}
