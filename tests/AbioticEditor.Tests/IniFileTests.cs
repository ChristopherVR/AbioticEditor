using AbioticEditor.Core.Ini;

namespace AbioticEditor.Tests;

/// <summary>
/// Duplicate-key ("the game appends rather than rewrites") semantics for <see cref="IniFile"/> /
/// <see cref="IniSection"/>: UE applies repeated <c>Key=Value</c> lines in file order, so the
/// LAST occurrence is the one actually in effect (see <c>SandboxSettings.ini</c>). Reads must
/// return that last value, and an in-place update must land on the same last line so the
/// game picks up the new value rather than reading past it to its own untouched last line.
/// </summary>
public sealed class IniFileTests
{
    private const string Original =
        "[SandboxSettings]\n" +
        "; keep this comment\n" +
        "EnemySpawnRate=1\n" +
        "EnemySpawnRate=2\n" +
        "EnemySpawnRate=3\n" +
        "bEnabled=True\n";

    [Fact]
    public void GetValue_OnARepeatedKey_ReturnsTheLastOccurrence()
    {
        var ini = IniFile.Parse(Original);
        var section = ini.FindSection("SandboxSettings")!;

        Assert.Equal("3", section.GetValue("EnemySpawnRate"));
        Assert.Equal(["1", "2", "3"], section.GetValues("EnemySpawnRate"));
    }

    [Fact]
    public void SetValue_OnARepeatedKey_UpdatesTheLastOccurrenceOnly_AndTheGameWouldReadIt()
    {
        var ini = IniFile.Parse(Original);
        var section = ini.FindSection("SandboxSettings")!;

        section.SetValue("EnemySpawnRate", "9");

        // Earlier duplicate lines, the comment, the unrelated key and its own line all stay
        // byte-identical; only the last EnemySpawnRate line's value changed.
        const string expected =
            "[SandboxSettings]\n" +
            "; keep this comment\n" +
            "EnemySpawnRate=1\n" +
            "EnemySpawnRate=2\n" +
            "EnemySpawnRate=9\n" +
            "bEnabled=True\n";
        Assert.Equal(expected, ini.ToText());

        // Prove the game would actually read the new value: reload the saved text into a fresh
        // IniFile (no reuse of the instance that made the edit) and re-apply the same last-wins
        // read the game's own ini loader uses.
        var reloaded = IniFile.Parse(ini.ToText());
        Assert.Equal("9", reloaded.FindSection("SandboxSettings")!.GetValue("EnemySpawnRate"));
    }

    [Fact]
    public void SetValue_OnASingleOccurrenceKey_UpdatesInPlace()
    {
        var ini = IniFile.Parse(Original);
        var section = ini.FindSection("SandboxSettings")!;

        section.SetValue("bEnabled", "False");

        Assert.Equal("False", section.GetValue("bEnabled"));
        Assert.Contains("bEnabled=False", ini.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("bEnabled=True", ini.ToText(), StringComparison.Ordinal);
    }
}
