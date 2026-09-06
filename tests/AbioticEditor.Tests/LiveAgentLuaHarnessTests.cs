using System.Diagnostics;
using System.IO;

namespace AbioticEditor.Tests;

/// <summary>
/// Runs the live-editing Lua mod's own stub-environment test harness
/// (<c>live-agent/AbioticEditorLiveAgentLua/tests/run.lua</c>) as part of the normal test suite,
/// so a regression in <c>main.lua</c> or any <c>areas/*.lua</c> module shows up here instead of
/// only being caught the next time someone happens to run the harness by hand.
/// </summary>
/// <remarks>
/// This needs an actual Lua 5.4 interpreter (the harness is plain Lua, not .NET) - see
/// <c>live-agent/README.md</c> for how one was built for this project (lua.org 5.4.7 source +
/// MSVC <c>cl</c>, no external package). CI and most dev machines do not have one on PATH, so
/// this test SKIPS (not fails) when none can be found, checking in order:
/// <list type="number">
///   <item><c>ABIOTIC_LUA_EXE</c> - an absolute path to a Lua 5.4 executable.</item>
///   <item><c>lua54</c>, <c>lua5.4</c>, <c>lua</c> - resolved via PATH.</item>
/// </list>
/// </remarks>
public sealed class LiveAgentLuaHarnessTests
{
    [SkippableFact]
    public void Stub_environment_harness_passes()
    {
        var luaExe = FindLuaExecutable();
        Skip.If(luaExe is null, "no Lua 5.4 interpreter found (set ABIOTIC_LUA_EXE, or put lua54/lua5.4/lua on PATH)");

        var repoRoot = FindRepoRoot();
        Skip.If(repoRoot is null, "could not locate the repo root (live-agent/AbioticEditorLiveAgentLua/tests/run.lua not found above the test binary)");

        var runScript = Path.Combine(repoRoot!, "live-agent", "AbioticEditorLiveAgentLua", "tests", "run.lua");

        var startInfo = new ProcessStartInfo
        {
            FileName = luaExe,
            ArgumentList = { runScript },
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        var exited = process.WaitForExit(TimeSpan.FromSeconds(30));

        // Surface the harness's own output either way - it names every loaded area and every
        // check it ran, which is exactly what a failure here needs to point at the right module.
        // A plain Console write (rather than ITestOutputHelper, which would need constructor
        // injection) still shows up with `dotnet test -v n` or on failure.
        Console.WriteLine(stdout);
        if (stderr.Length > 0) Console.WriteLine("stderr: " + stderr);

        Assert.True(exited, "the Lua harness did not exit within 30 seconds");
        Assert.Equal(0, process.ExitCode);
    }

    private static string? FindLuaExecutable()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ABIOTIC_LUA_EXE");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        var candidates = new[] { "lua54", "lua5.4", "lua" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathExt = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : [""];

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in candidates)
            {
                foreach (var ext in pathExt)
                {
                    var candidate = Path.Combine(dir, name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>Walks up from the test binary looking for the harness's own entry script, the
    /// same upward-walk pattern <see cref="Fixtures"/> uses for the save fixtures.</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "live-agent", "AbioticEditorLiveAgentLua", "tests", "run.lua");
            if (File.Exists(marker)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
