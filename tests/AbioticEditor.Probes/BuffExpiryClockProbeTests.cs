using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>Research dump: compare each fixture player's buff expiry values with the world clock beside it.</summary>
public class BuffExpiryClockProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void Dump_ExpiryVersusWorldClock()
    {
        var root = Fixtures.CascadeDir is null ? null : Path.GetFullPath(Path.Combine(Fixtures.CascadeDir, "..", "..", ".."));
        if (root is null) return;
        foreach (var player in Directory.EnumerateFiles(root, "Player_*.sav", SearchOption.AllDirectories))
        {
            var data = PlayerSaveReader.ReadFromFile(player);
            if (data.ActiveBuffs.Count == 0) continue;
            var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(player))!;
            output.WriteLine($"PLAYER {player}");
            foreach (var b in data.ActiveBuffs) output.WriteLine($"  buff {b.BuffRow} limb={b.ParentLimb} expire={b.ExpireTime}");
            foreach (var w in Directory.EnumerateFiles(worldDir, "WorldSave_*.sav").Take(40))
            {
                var world = WorldSaveReader.ReadFromFile(w);
                if (WorldSaveReader.ReadWorldClock(world.Raw) is { } c) output.WriteLine($"  world {Path.GetFileName(w)} timeSeconds={c.Seconds} day={c.Day} minutesPassed={world.MinutesPassed}");
            }
        }
    }
}
