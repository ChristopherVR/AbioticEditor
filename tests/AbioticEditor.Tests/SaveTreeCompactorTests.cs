using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

[CollectionDefinition("Save tree memory", DisableParallelization = true)]
public sealed class SaveTreeMemoryFixture;

/// <summary>
/// The loaded save shares its repeated names (<c>SaveTreeCompactor</c>): it must still write back
/// byte for byte, and it must take noticeably less memory than the library's own tree.
/// </summary>
// GC.GetTotalMemory measures the entire process, so other tests must not load or release
// saves between the baseline and retained-tree measurements.
[Collection("Save tree memory")]
public sealed class SaveTreeCompactorTests(ITestOutputHelper output)
{
    private static string? Facility() => Fixtures.ClientWorldSaves("WorldSave_Facility.sav")
        .FirstOrDefault(p => p.Contains("Cascade", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_compacted_save_writes_back_byte_for_byte()
    {
        if (Facility() is not { } path) return;
        var original = File.ReadAllBytes(path);
        var data = WorldSaveReader.ReadFromFile(path);
        using var written = new MemoryStream();
        data.Raw.WriteTo(written);
        Assert.True(original.AsSpan().SequenceEqual(written.ToArray()), "compacting must not change a single byte");
    }

    [Fact]
    public void A_compacted_save_takes_far_less_memory()
    {
        if (Facility() is not { } path) return;
        var bytes = File.ReadAllBytes(path);

        static long Measure(Func<object> load)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = GC.GetTotalMemory(true);
            var kept = load();
            var after = GC.GetTotalMemory(true);
            GC.KeepAlive(kept);
            return after - before;
        }

        var plain = Measure(() => SaveGame.LoadFrom(new MemoryStream(bytes)));
        var compacted = Measure(() => WorldSaveReader.ReadFromStream(new MemoryStream(bytes)));
        output.WriteLine($"library tree {plain / 1048576.0:F1} MB, compacted with typed view {compacted / 1048576.0:F1} MB");
        Assert.True(compacted < plain * 0.75, $"expected at least a quarter less: {plain} vs {compacted}");
    }
}
