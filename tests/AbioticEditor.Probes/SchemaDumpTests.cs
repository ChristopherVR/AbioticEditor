using System.IO;
using UeSaveGame.Json;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Schema discovery: dumps player and world saves to JSON so we can map which
/// properties hold inventory items, character stats, etc. The output goes to
/// <c>%TEMP%/abiotic-editor-schema/</c> for offline inspection.
/// </summary>
public class SchemaDumpTests
{
    private readonly ITestOutputHelper _output;

    public SchemaDumpTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DumpPlayerSaveAsJson()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var playerSave = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        if (!File.Exists(playerSave))
        {
            _output.WriteLine($"No player save at {playerSave}; skipping.");
            return;
        }

        var outDir = Path.Combine(Path.GetTempPath(), "abiotic-editor-schema");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "player.json");

        var serializer = new SaveGameSerializer();
        using (var input = File.OpenRead(playerSave))
        using (var output = File.Create(outFile))
        {
            serializer.ConvertToJson(input, output);
        }

        var size = new FileInfo(outFile).Length;
        _output.WriteLine($"Player save JSON dumped: {outFile} ({size:N0} bytes)");

        // Read top of file to show structure
        var top = File.ReadAllText(outFile).Substring(0, Math.Min(2000, (int)size));
        _output.WriteLine("--- first 2KB ---");
        _output.WriteLine(top);
    }

    [Fact]
    public void DumpWorldMetadataAsJson()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var save = Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav");
        if (!File.Exists(save)) return;

        var outDir = Path.Combine(Path.GetTempPath(), "abiotic-editor-schema");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "world-metadata.json");

        var serializer = new SaveGameSerializer();
        using (var input = File.OpenRead(save))
        using (var output = File.Create(outFile))
        {
            serializer.ConvertToJson(input, output);
        }

        _output.WriteLine($"World metadata JSON dumped: {outFile} ({new FileInfo(outFile).Length:N0} bytes)");
    }

    [Fact]
    public void DumpWorldFacilityAsJson()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var save = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(save))
        {
            _output.WriteLine($"No save at {save}; skipping.");
            return;
        }

        var outDir = Path.Combine(Path.GetTempPath(), "abiotic-editor-schema");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "world-facility.json");

        var serializer = new SaveGameSerializer();
        using (var input = File.OpenRead(save))
        using (var output = File.Create(outFile))
        {
            serializer.ConvertToJson(input, output);
        }

        _output.WriteLine($"World facility JSON dumped: {outFile} ({new FileInfo(outFile).Length:N0} bytes)");
    }

    [Fact]
    public void DumpWorldPlayerHouseAsJson()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var save = Path.Combine(Fixtures.CascadeDir!, "WorldSave_H_Cabin.sav");
        if (!File.Exists(save))
        {
            _output.WriteLine($"No save at {save}; skipping.");
            return;
        }

        var outDir = Path.Combine(Path.GetTempPath(), "abiotic-editor-schema");
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "world-h-cabin.json");

        var serializer = new SaveGameSerializer();
        using (var input = File.OpenRead(save))
        using (var output = File.Create(outFile))
        {
            serializer.ConvertToJson(input, output);
        }

        _output.WriteLine($"World H_Cabin JSON dumped: {outFile} ({new FileInfo(outFile).Length:N0} bytes)");
    }
}
