using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// A save the editor cannot understand (restructured by a game update, or a newer version) is
/// still readable but must never be written back; saves it does understand are unaffected.
/// </summary>
[Collection("SaveWriteGuard")]
public sealed class SaveWriteGuardTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "abiotic-guard-" + Guid.NewGuid().ToString("N"));

    public SaveWriteGuardTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SaveWriteGuard.OverrideEnvironmentVariable, null);
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private string CopyOf(string source)
    {
        var target = Path.Combine(_temp, Path.GetFileName(source));
        File.Copy(source, target);
        return target;
    }

    private static string? LegacyPlayer() => Fixtures.CascadeDir is null
        ? null
        : Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav")
            .Order(StringComparer.Ordinal).First();

    /// <summary>Renames modeled properties so the save looks like a restructured game layout.</summary>
    private static PlayerSaveData Restructured(string path)
    {
        var data = PlayerSaveReader.ReadFromFile(path);
        var members = PlayerSaveReader.GetCharacterSaveData(data.Raw)!;
        var renamed = 0;
        foreach (var tag in members)
        {
            if (renamed >= SaveLayoutProbe.LayoutChangeThreshold + 1) break;
            if (tag.Name?.Value is { } name && PlayerSaveReader.IsModeledKey(name))
            {
                typeof(UeSaveGame.FPropertyTag)
                    .GetField("<Name>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(tag, new UeSaveGame.FString("RenamedByGameUpdate" + renamed++));
            }
        }
        Assert.True(renamed > SaveLayoutProbe.LayoutChangeThreshold, "fixture has too few modeled properties to rename");
        return data;
    }

    [SkippableFact]
    public void Saves_the_editor_understands_are_not_blocked_and_still_write()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam fixture is not in this checkout");
        var path = CopyOf(LegacyPlayer()!);
        var data = PlayerSaveReader.ReadFromFile(path);

        Assert.Null(SaveWriteGuard.BlockReason(data.Raw));
        PlayerSaveWriter.WriteToFile(data, path);
        Assert.True(File.Exists(path + ".bak"));
    }

    [SkippableTheory]
    [InlineData("PlayerData/Player_76561197993781479.sav")]
    [InlineData("Validation/Player_76561197993781479.sav")]
    [InlineData("WorldSave_Facility.sav")]
    [InlineData("WorldSave_MetaData.sav")]
    public void Game_written_saves_from_the_supported_native_layout_are_never_blocked(string relative)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tests", "fixtures"))) d = d.Parent;
        var path = Path.Combine(d!.FullName, "tests", "fixtures", "Update1_5", relative);
        Skip.If(!File.Exists(path), "the Update 5 fixture is not in this checkout");

        var save = Path.GetFileName(path).StartsWith("Player_", StringComparison.Ordinal)
            ? PlayerSaveReader.ReadFromFile(path).Raw
            : WorldSaveReader.ReadFromFile(path).Raw;

        Assert.Null(SaveWriteGuard.BlockReason(save));
    }

    [SkippableFact]
    public void A_restructured_layout_is_reported_and_its_writes_are_refused_without_touching_the_file()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam fixture is not in this checkout");
        var path = CopyOf(LegacyPlayer()!);
        var before = File.ReadAllBytes(path);
        var data = Restructured(path);

        var report = CompatibilityAnalyzer.AnalyzePlayer(data);
        Assert.Equal(CompatibilitySeverity.LayoutChanged, report.Severity);
        Assert.NotNull(report.Warning);
        Assert.False(report.Operations.CanWrite(EditingArea.PlayerStats));
        Assert.True(report.Operations.CanInspect);

        var blocked = Assert.Throws<SaveWriteBlockedException>(() => PlayerSaveWriter.WriteToFile(data, path));
        Assert.Equal(CompatibilitySeverity.LayoutChanged, blocked.Severity);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [SkippableFact]
    public void A_newer_save_version_is_refused_for_world_saves()
    {
        Skip.If(Fixtures.ServerWorldsDir is null, "the dedicated-server fixture is not in this checkout");
        var path = CopyOf(Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_MetaData.sav"));
        var before = File.ReadAllBytes(path);
        var data = WorldSaveReader.ReadFromFile(path);
        var newer = SaveVersionRegistry.Find(SaveKind.Metadata)!.MaxKnownVersion!.Value + 1;
        Assert.True(SaveVersionRegistry.TrySetAbfVersion(data.Raw, newer));

        Assert.Throws<SaveWriteBlockedException>(() => WorldSaveWriter.WriteToFile(data, path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [SkippableFact]
    public void The_override_variable_lets_an_advanced_user_write_anyway()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam fixture is not in this checkout");
        var path = CopyOf(LegacyPlayer()!);
        var data = Restructured(path);
        Assert.NotNull(SaveWriteGuard.BlockReason(data.Raw));

        Environment.SetEnvironmentVariable(SaveWriteGuard.OverrideEnvironmentVariable, "1");
        Assert.Null(SaveWriteGuard.BlockReason(data.Raw));
        PlayerSaveWriter.WriteToFile(data, path);
    }
}
