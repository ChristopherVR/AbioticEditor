using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class RawJsonSessionTests
{
    [Fact]
    public async Task Player_complete_json_export_and_import_create_a_backup()
    {
        if (Fixtures.CascadeDir is null) return;
        var source = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        var path = CopyToTemporaryFile(source);
        try
        {
            var session = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
            await session.ExportJsonToFileAsync();
            Assert.True(session.JsonFileExists);
            Assert.True(new FileInfo(session.JsonPath).Length > 0);

            await session.ImportJsonFromFileAsync();
            Assert.True(File.Exists(path + ".bak"));
            _ = PlayerSaveReader.ReadFromFile(path);
        }
        finally { DeleteTemporaryFiles(path); }
    }

    [Fact]
    public async Task World_complete_json_export_and_import_create_a_backup()
    {
        if (Fixtures.CascadeDir is null) return;
        var source = Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav");
        var path = CopyToTemporaryFile(source);
        try
        {
            var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(path), path);
            await session.ExportJsonToFileAsync();
            Assert.True(session.JsonFileExists);

            await session.ImportJsonFromFileAsync();
            Assert.True(File.Exists(path + ".bak"));
            _ = WorldSaveReader.ReadFromFile(path);
        }
        finally { DeleteTemporaryFiles(path); }
    }

    private static string CopyToTemporaryFile(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"abiotic-raw-{Guid.NewGuid():N}.sav");
        File.Copy(source, path);
        return path;
    }

    private static void DeleteTemporaryFiles(string path)
    {
        foreach (var candidate in new[] { path, path + ".bak", path + ".json" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }
}
