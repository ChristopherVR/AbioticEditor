using System.IO;

using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Samples.VersionShim;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers the forward-compatibility load path: a save whose version field the editor can't
/// read is recovered by a registered <see cref="ISaveUpgrader"/> (here the real VersionShim
/// sample). The fixture is copied and deliberately corrupted so the test is deterministic.
/// </summary>
public class SaveUpgradeServiceTests
{
    private const int SaveGameVersionOffset = 4;
    private static readonly IPluginLog Log = new NullPluginLog();
    private static readonly IReadOnlyList<ISaveUpgrader> Upgraders = new ISaveUpgrader[] { new FixSaveVersionUpgrader() };

    private static string? AnySave()
        => Fixtures.CascadeDir is { } c && File.Exists(Path.Combine(c, "WorldSave_MetaData.sav"))
            ? Path.Combine(c, "WorldSave_MetaData.sav")
            : null;

    [Fact]
    public async Task LoadAsync_ValidSave_LoadsWithoutUpgrade()
    {
        var source = AnySave();
        if (source is null) return;

        using var temp = new TempFile(source);
        var result = await SaveUpgradeService.LoadAsync(temp.Path, Upgraders, Log);

        Assert.False(result.WasUpgraded);
        Assert.NotNull(result.Save);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedVersion_RecoveredByUpgrader()
    {
        var source = AnySave();
        if (source is null) return;

        using var temp = new TempFile(source);

        // Corrupt the SaveGameVersion field to an unsupported value so the normal parse throws.
        var bytes = File.ReadAllBytes(temp.Path);
        var original = BitConverter.ToInt32(bytes, SaveGameVersionOffset);
        BitConverter.GetBytes(99).CopyTo(bytes, SaveGameVersionOffset);
        File.WriteAllBytes(temp.Path, bytes);

        var result = await SaveUpgradeService.LoadAsync(temp.Path, Upgraders, Log, persist: true);

        Assert.True(result.WasUpgraded);
        Assert.Equal("fix-save-version", result.UpgraderId);
        Assert.NotNull(result.Save);
        Assert.True(result.Persisted);

        // The original (corrupted) bytes were preserved under the pre-upgrade backup, and the
        // file on disk now loads cleanly (version field repaired to the original value).
        Assert.True(File.Exists(temp.Path + ".preupgrade.bak"));
        var reloaded = await SaveUpgradeService.LoadAsync(temp.Path, Upgraders, Log);
        Assert.False(reloaded.WasUpgraded);
        Assert.Equal(original, BitConverter.ToInt32(File.ReadAllBytes(temp.Path), SaveGameVersionOffset));
    }

    [Fact]
    public async Task LoadAsync_NoUpgraderHandles_RethrowsLoadFailure()
    {
        var source = AnySave();
        if (source is null) return;

        using var temp = new TempFile(source);
        var bytes = File.ReadAllBytes(temp.Path);
        BitConverter.GetBytes(99).CopyTo(bytes, SaveGameVersionOffset);
        File.WriteAllBytes(temp.Path, bytes);

        // No upgraders registered -> the load failure is surfaced, not swallowed.
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SaveUpgradeService.LoadAsync(temp.Path, Array.Empty<ISaveUpgrader>(), Log));
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string source)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"abiotic-upgrade-{Guid.NewGuid():N}.sav");
            File.Copy(source, Path, overwrite: true);
        }

        public string Path { get; }

        public void Dispose()
        {
            TryDelete(Path);
            TryDelete(Path + ".preupgrade.bak");
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
