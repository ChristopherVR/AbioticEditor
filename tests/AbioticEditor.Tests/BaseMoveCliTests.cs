using AbioticEditor.Cli;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// <c>world object export|import</c>: a base is written to a base file and taken out of one world
/// with <c>--remove</c>, then put into a second world, through the real command tree on temp copies.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class BaseMoveCliTests
{
    static BaseMoveCliTests()
    {
        Environment.SetEnvironmentVariable("ABIOTIC_NO_PLUGINS", "1");
    }

    private static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await CommandTree.Build().Parse(args).InvokeAsync();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    [Fact]
    public async Task Export_with_remove_then_import_moves_the_pieces_between_two_worlds()
    {
        if (!HasServerFacility) return;
        var root = Path.Combine(Path.GetTempPath(), "abf-cli-move-" + Guid.NewGuid().ToString("N"));
        var here = Path.Combine(root, "Cascade", "WorldSave_Facility.sav");
        var there = Path.Combine(root, "NewWorld", "WorldSave_Facility.sav");
        Directory.CreateDirectory(Path.GetDirectoryName(here)!);
        Directory.CreateDirectory(Path.GetDirectoryName(there)!);
        File.WriteAllBytes(here, OriginalBytes);
        File.WriteAllBytes(there, OriginalBytes);
        try
        {
            var keys = PlayerBuilt(Load()).Where(o => o.StoredItemCount > 0).Take(3).Select(o => o.Key).ToList();
            var file = Path.Combine(root, "base" + BasePackage.FileExtension);

            var dry = await Run(["world", "object", "export", here, "--key", string.Join(',', keys), "--out", file, "--remove", "--dry-run"]);
            Assert.Equal(0, dry.Exit);
            Assert.False(File.Exists(file));
            Assert.Equal(OriginalBytes, File.ReadAllBytes(here));

            var export = await Run(["world", "object", "export", here, "--key", string.Join(',', keys), "--out", file, "--remove"]);
            Assert.True(export.Exit == 0, export.Err);
            Assert.True(File.Exists(file));
            var left = WorldSaveReader.ReadFromFile(here);
            Assert.All(keys, k => Assert.Null(WorldMapAccessor.FindEntry(left.Raw, "DeployedObjectMap", k)));

            var import = await Run(["world", "object", "import", there, "--file", file]);
            Assert.True(import.Exit == 0, import.Err);
            var arrived = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(there)).Objects!.Count;
            Assert.Equal(PlacedObjectCensus.Build(Load()).Objects!.Count + keys.Count, arrived);

            var bad = await Run(["world", "object", "import", there, "--file", here]);
            Assert.NotEqual(0, bad.Exit);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
