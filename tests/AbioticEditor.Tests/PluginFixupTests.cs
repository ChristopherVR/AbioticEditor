using System.IO;

using AbioticEditor.Core.Plugins;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Samples.GrantFlag;
using AbioticEditor.Samples.RepairNeeds;

namespace AbioticEditor.Tests;

/// <summary>
/// End-to-end coverage of the sample "fix-up" plugins driven through the real
/// <see cref="SaveOperationRunner"/>: load -> kind check -> execute -> backup + write. Each
/// test operates on a throwaway copy of a fixture so the fixtures are never mutated.
/// </summary>
public class PluginFixupTests
{
    private static readonly IPluginLog Log = new NullPluginLog();

    private static string? PlayerSave()
    {
        var dir = Fixtures.CascadeDir is { } c ? Path.Combine(c, "PlayerData") : null;
        if (dir is null || !Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "Player_*.sav").FirstOrDefault(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
    }

    private static string? WorldSave()
        => Fixtures.CascadeDir is { } c && File.Exists(Path.Combine(c, "WorldSave_Facility.sav"))
            ? Path.Combine(c, "WorldSave_Facility.sav")
            : null;

    [Fact]
    public async Task RepairNeeds_RestoresAllNeedsToFull_AndWritesWithBackup()
    {
        var source = PlayerSave();
        if (source is null) return;

        using var temp = new TempSave(source);
        var outcome = await SaveOperationRunner.RunAsync(new RepairNeedsOperation(), temp.Path, null, Log);

        Assert.True(outcome.Result.Success);
        Assert.Equal(SaveKind.Player, outcome.Kind);

        // Whether or not the fixture needed repair, the post-condition is full needs on disk.
        var reloaded = PlayerSaveReader.ReadFromFile(temp.Path).Stats;
        Assert.Equal(100, reloaded.Hunger);
        Assert.Equal(100, reloaded.Thirst);
        Assert.Equal(100, reloaded.Sanity);
        Assert.Equal(100, reloaded.Fatigue);
        Assert.Equal(100, reloaded.Continence);

        // A real write leaves a .bak; a no-change run does not.
        Assert.Equal(outcome.Wrote, File.Exists(temp.Path + ".bak"));
    }

    [Fact]
    public async Task RepairNeeds_DryRun_DoesNotTouchTheFile()
    {
        var source = PlayerSave();
        if (source is null) return;

        using var temp = new TempSave(source);
        var before = File.ReadAllBytes(temp.Path);

        var outcome = await SaveOperationRunner.RunAsync(new RepairNeedsOperation(), temp.Path, null, Log, dryRun: true);

        Assert.False(outcome.Wrote);
        Assert.False(File.Exists(temp.Path + ".bak"));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(temp.Path)), "dry run must not modify the file");
    }

    [Fact]
    public async Task RepairNeeds_OnWorldSave_IsRejectedByKindGuard()
    {
        var world = WorldSave();
        if (world is null) return;

        using var temp = new TempSave(world);
        var outcome = await SaveOperationRunner.RunAsync(new RepairNeedsOperation(), temp.Path, null, Log);

        Assert.False(outcome.Result.Success);
        Assert.False(outcome.Wrote);
    }

    [Fact]
    public async Task GrantFlag_AddsMissingFlag_ThenIsIdempotent()
    {
        var world = WorldSave();
        if (world is null) return;

        const string flag = "AbioticEditor_FixupTest_Flag";
        using var temp = new TempSave(world);
        var parameters = new Dictionary<string, string> { ["flag"] = flag };

        // First run adds the flag and persists it.
        var first = await SaveOperationRunner.RunAsync(new GrantFlagOperation(), temp.Path, parameters, Log);
        Assert.True(first.Result.Success);
        Assert.True(first.Wrote);
        Assert.Contains(flag, WorldSaveReader.ReadFromFile(temp.Path).Flags);

        // Second run finds it already present: success, no change, no write.
        var second = await SaveOperationRunner.RunAsync(new GrantFlagOperation(), temp.Path, parameters, Log);
        Assert.True(second.Result.Success);
        Assert.False(second.Wrote);
        Assert.Equal(0, second.Result.ChangeCount);
    }

    [Fact]
    public async Task GrantFlag_WithoutRequiredParameter_Fails()
    {
        var world = WorldSave();
        if (world is null) return;

        using var temp = new TempSave(world);
        var outcome = await SaveOperationRunner.RunAsync(new GrantFlagOperation(), temp.Path, null, Log);

        Assert.False(outcome.Result.Success);
        Assert.False(outcome.Wrote);
    }

    /// <summary>A unique temp copy of a fixture save, cleaned up (with any .bak) on dispose.</summary>
    private sealed class TempSave : IDisposable
    {
        public TempSave(string source)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"abiotic-fixup-{Guid.NewGuid():N}.sav");
            File.Copy(source, Path, overwrite: true);
        }

        public string Path { get; }

        public void Dispose()
        {
            TryDelete(Path);
            TryDelete(Path + ".bak");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    private sealed class NullPluginLog : IPluginLog
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
