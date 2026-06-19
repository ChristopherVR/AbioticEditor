using System.IO;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="WorldSaveFactory.CreateMinimalRegion"/>: crafting a placeholder
/// <c>WorldSave_&lt;region&gt;.sav</c> for a region a save hasn't visited yet, so story /
/// quest-flag edits that reference it have a real world save to target. These tests use the
/// embedded blank-region template (no fixture or installed game required).
/// </summary>
public sealed class WorldSaveFactoryTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-region-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    private static string? ReadSaveIdentifier(string path)
        => WorldSaveReader.ReadFromFile(path).Raw.Properties
            .FindByPrefix("SaveIdentifier")?.Property?.Value?.ToString();

    [Fact]
    public void CreateMinimalRegion_writes_named_file_that_round_trips()
    {
        using var dir = new TempDir();

        var path = WorldSaveFactory.CreateMinimalRegion(dir.Path, "V_DistantShore");

        Assert.Equal(Path.Combine(dir.Path, "WorldSave_V_DistantShore.sav"), path);
        Assert.True(File.Exists(path));

        // The crafted file re-reads through the real reader without throwing, and its identity
        // is the requested region (not the template's).
        var data = WorldSaveReader.ReadFromFile(path);
        Assert.NotNull(data.Raw);
        Assert.Equal("V_DistantShore", ReadSaveIdentifier(path));
    }

    [Theory]
    [InlineData("WorldSave_Facility_Office9.sav", "WorldSave_Facility_Office9.sav")]
    [InlineData("WorldSave_Facility_Office9", "WorldSave_Facility_Office9.sav")]
    [InlineData("Facility_Office9.sav", "WorldSave_Facility_Office9.sav")]
    [InlineData("  Facility_Office9  ", "WorldSave_Facility_Office9.sav")]
    public void CreateMinimalRegion_normalizes_the_region_token(string input, string expectedFileName)
    {
        using var dir = new TempDir();

        var path = WorldSaveFactory.CreateMinimalRegion(dir.Path, input);

        Assert.Equal(expectedFileName, Path.GetFileName(path));
        Assert.Equal("Facility_Office9", ReadSaveIdentifier(path));
    }

    [Fact]
    public void CreateMinimalRegion_refuses_to_overwrite_an_existing_region()
    {
        using var dir = new TempDir();
        WorldSaveFactory.CreateMinimalRegion(dir.Path, "V_Foo");

        Assert.Throws<IOException>(() => WorldSaveFactory.CreateMinimalRegion(dir.Path, "V_Foo"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMinimalRegion_rejects_unsafe_or_empty_tokens(string bad)
    {
        using var dir = new TempDir();

        Assert.Throws<ArgumentException>(() => WorldSaveFactory.CreateMinimalRegion(dir.Path, bad));
        Assert.Empty(Directory.EnumerateFiles(dir.Path));
    }

    [Fact]
    public void CreateMinimalRegion_creates_the_world_folder_if_absent()
    {
        using var dir = new TempDir();
        var nested = Path.Combine(dir.Path, "Worlds", "NewWorld");

        var path = WorldSaveFactory.CreateMinimalRegion(nested, "V_Train");

        Assert.True(File.Exists(path));
        Assert.Equal(nested, Path.GetDirectoryName(path));
    }
}
