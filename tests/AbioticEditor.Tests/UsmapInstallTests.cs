using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="GameAssetProvider.InstallUserMappings"/> - the app's
/// "import a newer usmap" feature for future game versions.
/// </summary>
public class UsmapInstallTests
{
    [Fact]
    public void Installs_valid_usmap_to_target()
    {
        var dir = Directory.CreateTempSubdirectory("usmap-test");
        try
        {
            var source = Path.Combine(dir.FullName, "NewDump.usmap");
            // Valid usmap magic 0xC4 0x30 followed by arbitrary payload.
            File.WriteAllBytes(source, new byte[] { 0xC4, 0x30, 0x01, 0x02, 0x03 });
            var target = Path.Combine(dir.FullName, "override", "Mappings.usmap");

            var installed = GameAssetProvider.InstallUserMappings(source, target);

            Assert.Equal(target, installed);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Overwrites_existing_override()
    {
        var dir = Directory.CreateTempSubdirectory("usmap-test");
        try
        {
            var target = Path.Combine(dir.FullName, "Mappings.usmap");
            File.WriteAllBytes(target, new byte[] { 0xC4, 0x30, 0xAA });
            var source = Path.Combine(dir.FullName, "Newer.usmap");
            File.WriteAllBytes(source, new byte[] { 0xC4, 0x30, 0xBB, 0xCC });

            GameAssetProvider.InstallUserMappings(source, target);

            Assert.Equal(new byte[] { 0xC4, 0x30, 0xBB, 0xCC }, File.ReadAllBytes(target));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Rejects_non_usmap_file()
    {
        var dir = Directory.CreateTempSubdirectory("usmap-test");
        try
        {
            var source = Path.Combine(dir.FullName, "NotAUsmap.usmap");
            File.WriteAllText(source, "GVAS or whatever else");
            var target = Path.Combine(dir.FullName, "Mappings.usmap");

            Assert.Throws<InvalidDataException>(
                () => GameAssetProvider.InstallUserMappings(source, target));
            Assert.False(File.Exists(target));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Rejects_missing_source()
    {
        Assert.Throws<FileNotFoundException>(
            () => GameAssetProvider.InstallUserMappings(
                Path.Combine(Path.GetTempPath(), "does-not-exist-12345.usmap"),
                Path.Combine(Path.GetTempPath(), "never-written.usmap")));
    }

    [Fact]
    public void Bundled_usmap_has_expected_magic()
    {
        // Guards the magic check itself against a wrong constant: the real bundled
        // mappings file must pass the same validation the import uses.
        string? bundled = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "dotnet", "assets", "Mappings.usmap");
            if (File.Exists(candidate)) { bundled = candidate; break; }
            candidate = Path.Combine(dir.FullName, "assets", "Mappings.usmap");
            if (File.Exists(candidate)) { bundled = candidate; break; }
        }
        Assert.True(bundled is not null, "expected the bundled usmap at dotnet/assets/Mappings.usmap");

        var tempDir = Directory.CreateTempSubdirectory("usmap-test");
        try
        {
            var target = Path.Combine(tempDir.FullName, "Mappings.usmap");
            GameAssetProvider.InstallUserMappings(bundled, target);
            Assert.True(File.Exists(target));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
