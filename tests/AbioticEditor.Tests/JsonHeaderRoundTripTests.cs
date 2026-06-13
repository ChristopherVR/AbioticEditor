using System.IO;
using AbioticEditor.Core;
using UeSaveGame;
using Xunit.Abstractions;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Tests;

/// <summary>
/// Regression: the JSON export/import path must preserve the custom ABF_SAVE_VERSION
/// header (it used to re-import as Version=0 - silent corruption).
/// </summary>
public class JsonHeaderRoundTripTests
{
    private readonly ITestOutputHelper _output;
    public JsonHeaderRoundTripTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void JsonRoundTrip_PreservesAbfHeader()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var source = Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav");
        Assert.True(File.Exists(source));

        using var fs = File.OpenRead(source);
        var save = SaveGame.LoadFrom(fs);

        var json = SaveJsonBridge.ToJson(save);
        Assert.Contains("CustomHeader", json);
        _output.WriteLine(json[..Math.Min(600, json.Length)]);

        var temp = Path.Combine(Path.GetTempPath(), $"json-header-{Guid.NewGuid():N}.sav");
        try
        {
            SaveJsonBridge.ApplyJsonToFile(json, temp);

            var original = ReadAbfHeader(File.ReadAllBytes(source));
            var roundTripped = ReadAbfHeader(File.ReadAllBytes(temp));
            _output.WriteLine($"original  ver={original.Version} id={original.Id}");
            _output.WriteLine($"reimport  ver={roundTripped.Version} id={roundTripped.Id}");

            Assert.True(original.Version > 0, "fixture should carry a non-zero ABF version");
            Assert.Equal(original.Version, roundTripped.Version);
            Assert.Equal(original.Id, roundTripped.Id);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Finds "ABF_SAVE_VERSION" and reads the two ints after its terminator.</summary>
    private static (int Version, int Id) ReadAbfHeader(byte[] bytes)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes("ABF_SAVE_VERSION");
        for (var i = 0; i <= bytes.Length - needle.Length - 13; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j]) { match = false; break; }
            }
            if (!match) continue;

            var after = i + needle.Length + 1; // skip NUL terminator
            var version = BitConverter.ToInt32(bytes, after);
            var id = BitConverter.ToInt32(bytes, after + 4);
            return (version, id);
        }
        throw new InvalidDataException("ABF_SAVE_VERSION marker not found");
    }
}
