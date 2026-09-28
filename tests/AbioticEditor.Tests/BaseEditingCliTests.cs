using System.Text.Json;
using AbioticEditor.Cli;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Every write path of <c>world object move|rotate|delete|duplicate</c> exercised through the real command
/// tree on a temp copy of a fixture: dry-run writes nothing, a real run writes through the backup path
/// (<c>.bak</c> equals the original), and a refusal exits 1 and leaves the file untouched.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class BaseEditingCliTests
{
    static BaseEditingCliTests()
    {
        Environment.SetEnvironmentVariable("ABIOTIC_NO_PLUGINS", "1");
    }

    private sealed record CliResult(int Exit, string Out, string Err);

    private static async Task<CliResult> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await CommandTree.Build().Parse(args).InvokeAsync();
            return new CliResult(exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    private static string TempSave(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "abf-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "WorldSave_Facility.sav");
        File.WriteAllBytes(path, OriginalBytes);
        return path;
    }

    [Fact]
    public async Task Move_dry_run_writes_nothing_and_a_real_run_moves_and_keeps_a_backup()
    {
        if (!HasServerFacility) return;
        var key = BaseEditingTests.Quiet(Load());
        var path = TempSave(out var dir);
        try
        {
            var dry = await Run("world", "object", "move", path, "--key", key, "--dx", "150", "--dz=-20", "--dry-run");
            Assert.Equal(0, dry.Exit);
            Assert.Contains("Dry run", dry.Out, StringComparison.Ordinal);
            Assert.Contains(key, dry.Out, StringComparison.Ordinal);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
            Assert.False(File.Exists(path + ".bak"));

            var before = PlacedObjectCensus.ReadTransform(Entry(Load(), key))!.Translation!.Value;
            var real = await Run("world", "object", "move", path, "--key", key, "--dx", "150", "--dz=-20");
            Assert.Equal(0, real.Exit);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path + ".bak")));
            var after = PlacedObjectCensus.ReadTransform(Entry(WorldSaveReader.ReadFromFile(path), key))!.Translation!.Value;
            Assert.Equal(before.X + 150, after.X);
            Assert.Equal(before.Z - 20, after.Z);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rotate_turns_a_group_around_its_centroid_and_reports_unknown_pivots()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var keys = PlayerBuilt(data).Where(o => o.Transform is { Translation: not null, Rotation: not null })
            .Take(3).Select(o => o.Key).ToList();
        var path = TempSave(out var dir);
        try
        {
            var bad = await Run("world", "object", "rotate", path, "--key", string.Join(',', keys), "--yaw", "90", "--pivot", "nonsense");
            Assert.Equal(1, bad.Exit);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)));

            var real = await Run("world", "object", "rotate", path, "--key", string.Join(',', keys), "--yaw", "90");
            Assert.Equal(0, real.Exit);
            var after = WorldSaveReader.ReadFromFile(path);
            foreach (var k in keys)
            {
                var yawBefore = PlacedObjectCensus.ReadTransform(Entry(data, k))!.Rotation!.Value.YawDegrees;
                var yawAfter = PlacedObjectCensus.ReadTransform(Entry(after, k))!.Rotation!.Value.YawDegrees;
                Assert.InRange(Math.Abs(PlacementMath.Normalize180(yawAfter - yawBefore - 90)), 0, 1e-6);
            }
            Assert.True(File.Exists(path + ".bak"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicate_previews_as_text_and_json_then_writes_copies_with_a_backup()
    {
        if (!HasServerFacility) return;
        var (owner, device, _) = BaseEditingTests.LinkedPair(Load());
        var path = TempSave(out var dir);
        try
        {
            var dry = await Run("world", "object", "duplicate", path, "--key", owner, "--key", device, "--dx", "500", "--dry-run");
            Assert.Equal(0, dry.Exit);
            Assert.Contains("copy", dry.Out, StringComparison.Ordinal);
            Assert.Contains("outlet", dry.Out, StringComparison.Ordinal);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)));

            var json = await Run("world", "object", "duplicate", path, "--key", owner, "--key", device, "--dx", "500", "--dry-run", "--json");
            using (var doc = JsonDocument.Parse(json.Out))
            {
                Assert.Equal(2, doc.RootElement.GetProperty("Duplications").GetArrayLength());
                Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("Issues")[0].GetProperty("Severity").ValueKind);
            }

            var count = WorldMapAccessor.Entries(Load().Raw, "DeployedObjectMap").Count();
            var real = await Run("world", "object", "duplicate", path, "--key", owner, "--key", device, "--dx", "500");
            Assert.Equal(0, real.Exit);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path + ".bak")));
            var after = WorldSaveReader.ReadFromFile(path);
            Assert.Equal(count + 2, WorldMapAccessor.Entries(after.Raw, "DeployedObjectMap").Count());
            Assert.Contains("copied", real.Out, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicate_copy_contents_flag_and_policies_reach_the_model()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var crate = PlayerBuilt(data).First(o => o.StoredItemCount > 1 && o.ClassName!.Contains("StorageCrate", StringComparison.Ordinal)
            && !o.ClassName.Contains("Void", StringComparison.Ordinal));
        var (owner, _, _) = BaseEditingTests.LinkedPair(data);
        var path = TempSave(out var dir);
        try
        {
            var refused = await Run("world", "object", "duplicate", path, "--key", owner, "--external-power", "refuse");
            Assert.Equal(1, refused.Exit);
            Assert.Contains("external-power-link", refused.Err, StringComparison.Ordinal);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
            Assert.False(File.Exists(path + ".bak"));

            var copy = await Run("world", "object", "duplicate", path, "--key", crate.Key, "--copy-contents", "--dx", "300");
            Assert.Equal(0, copy.Exit);
            var after = WorldSaveReader.ReadFromFile(path);
            var created = WorldMapAccessor.Entries(after.Raw, "DeployedObjectMap").Last().Key;
            Assert.Equal(
                PlacedObjectContents.ReadItems(Entry(data, crate.Key)),
                PlacedObjectContents.ReadItems(Entry(after, created)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_is_refused_with_an_inbound_plug_and_succeeds_with_an_explicit_policy()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var inbound = Sockets(data).First(s => s.Plugged is not null && built.Contains(s.Plugged) && s.Owner != s.Plugged);
        var path = TempSave(out var dir);
        try
        {
            var dry = await Run("world", "object", "delete", path, "--key", inbound.Plugged!, "--dry-run");
            Assert.Equal(1, dry.Exit);
            Assert.Contains("REFUSED", dry.Out, StringComparison.Ordinal);
            Assert.Contains("inbound-plug", dry.Out, StringComparison.Ordinal);

            var refused = await Run("world", "object", "delete", path, "--key", inbound.Plugged!);
            Assert.Equal(1, refused.Exit);
            Assert.Contains("inbound-plug", refused.Err, StringComparison.Ordinal);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
            Assert.False(File.Exists(path + ".bak"));

            var ok = await Run("world", "object", "delete", path, "--key", inbound.Plugged!,
                "--inbound-plugs", "drop", "--other-references", "keep", "--bed-claims", "keep", "--teleporter-peers", "keep");
            Assert.Equal(0, ok.Exit);
            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path + ".bak")));
            var after = WorldSaveReader.ReadFromFile(path);
            Assert.Null(WorldMapAccessor.FindEntry(after.Raw, "DeployedObjectMap", inbound.Plugged!));
            Assert.Equal("-1", SocketPlugged(after, inbound.Id));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_of_a_level_placed_object_or_a_bad_argument_is_a_user_error_that_writes_nothing()
    {
        if (!HasServerFacility) return;
        var levelKey = PlacedObjectCensus.Build(Load()).Objects!.First(o => o.Key.Length != 32).Key;
        var path = TempSave(out var dir);
        try
        {
            var level = await Run("world", "object", "delete", path, "--key", levelKey);
            Assert.Equal(1, level.Exit);
            Assert.Contains("level-placed", level.Err, StringComparison.Ordinal);

            var badPolicy = await Run("world", "object", "delete", path, "--key", "AAAA", "--owned-sockets", "maybe");
            Assert.Equal(1, badPolicy.Exit);

            var missing = await Run("world", "object", "move", Path.Combine(dir, "nope.sav"), "--key", "AAAA", "--dx", "1");
            Assert.Equal(1, missing.Exit);

            Assert.True(OriginalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
            Assert.False(File.Exists(path + ".bak"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_scans_sibling_saves_for_references_unless_told_not_to()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var key = BaseEditingTests.Quiet(data);
        var path = TempSave(out var dir);
        try
        {
            // A sibling save in the folder is scanned (read only) and never modified.
            var sibling = Path.Combine(dir, "WorldSave_Facility_Pool.sav");
            File.Copy(Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility_Pool.sav"), sibling);
            var siblingBytes = File.ReadAllBytes(sibling);
            var res = await Run("world", "object", "delete", path, "--key", key, "--dry-run");
            Assert.Equal(0, res.Exit);
            Assert.DoesNotContain("Only this save was scanned", res.Out, StringComparison.Ordinal);
            var noScan = await Run("world", "object", "delete", path, "--key", key, "--dry-run", "--no-scan");
            Assert.Contains("Only this save was scanned", noScan.Out, StringComparison.Ordinal);

            var real = await Run("world", "object", "delete", path, "--key", key);
            Assert.Equal(0, real.Exit);
            Assert.True(siblingBytes.AsSpan().SequenceEqual(File.ReadAllBytes(sibling)));
            Assert.NotEmpty(built);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
