using AbioticEditor.Core.LiveEditing;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="LiveAgentPaths"/>'s pure path logic: where a portable data folder
/// (<c>ABIOTIC_APPDATA_DIR</c>) moves live editing's working files, and which paths are safe to
/// hand the native helper and the in-game Lua mod.
/// </summary>
public class LiveAgentPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveRedirectedRoot_is_null_without_an_override(string? value)
        => Assert.Null(LiveAgentPaths.ResolveRedirectedRoot(value));

    [Fact]
    public void ResolveRedirectedRoot_is_an_absolute_live_agent_folder_under_the_override()
    {
        var root = LiveAgentPaths.ResolveRedirectedRoot(Path.Combine("portable", "data"));

        Assert.NotNull(root);
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.Equal(Path.Combine(Path.GetFullPath(Path.Combine("portable", "data")), "live-agent"), root);
    }

    [Theory]
    [InlineData(@"D:\Portable Apps\AbioticEditor\data\live-agent", true)]
    [InlineData(@"D:\Bücher\live-agent", false)]
    [InlineData(@"D:\odd]==]name\live-agent", false)]
    [InlineData("D:\\line\nbreak", false)]
    [InlineData("", false)]
    public void IsSafeForNativeSides_accepts_only_plain_printable_paths(string path, bool expected)
        => Assert.Equal(expected, LiveAgentPaths.IsSafeForNativeSides(path));

    [Fact]
    public void LuaPointerContent_returns_the_folder_as_a_raw_Lua_string()
    {
        var content = LiveAgentPaths.LuaPointerContent(@"D:\Portable\data\live-agent");

        // A long-bracket string: backslashes must reach Lua unescaped.
        Assert.EndsWith("return [==[" + @"D:\Portable\data\live-agent" + "]==]\n", content);
        Assert.All(content.Split('\n', StringSplitOptions.RemoveEmptyEntries)[..^1], line => Assert.StartsWith("--", line));
    }

    [Fact]
    public void Test_runs_keep_live_agent_files_out_of_the_real_profile()
    {
        // TestEnvironmentIsolation points ABIOTIC_APPDATA_DIR at a throwaway folder.
        var overridden = Environment.GetEnvironmentVariable("ABIOTIC_APPDATA_DIR");
        if (string.IsNullOrWhiteSpace(overridden)) return;

        Assert.Equal(LiveAgentPaths.ResolveRedirectedRoot(overridden), LiveAgentPaths.HostRoot);
    }
}
