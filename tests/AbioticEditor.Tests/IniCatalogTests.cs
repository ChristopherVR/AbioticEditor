using System.IO;
using AbioticEditor.Core.Ini;

namespace AbioticEditor.Tests;

/// <summary>
/// Unit tests for <see cref="AbioticIniCatalog.Discover"/> covering cross-folder isolation.
/// Uses a temporary in-memory directory tree so no fixtures are needed.
/// </summary>
public class IniCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uesave-inicatalog-{Guid.NewGuid():N}");

    public IniCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ---------- helpers ----------

    private string MkDir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private void Touch(params string[] parts)
        => File.WriteAllText(Path.Combine([_root, .. parts]), string.Empty);

    // ---------- tests ----------

    [Fact]
    public void Discover_ServerRoot_OnlyIncludesCurrentWorldSandboxSettings()
    {
        // server_root/
        //   Admin.ini
        //   Worlds/
        //     WorldA/  SandboxSettings.ini   <-- loaded
        //     WorldB/  SandboxSettings.ini   <-- must NOT leak
        Touch("Admin.ini");
        MkDir("Worlds", "WorldA");
        Touch("Worlds", "WorldA", "SandboxSettings.ini");
        MkDir("Worlds", "WorldB");
        Touch("Worlds", "WorldB", "SandboxSettings.ini");

        var results = AbioticIniCatalog.Discover(Path.Combine(_root, "Worlds", "WorldA"));

        var sandboxPaths = results
            .Where(f => f.Kind == AbioticIniKind.SandboxSettings)
            .Select(f => f.FullPath)
            .ToList();

        Assert.Single(sandboxPaths);
        Assert.Contains("WorldA", sandboxPaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorldB", sandboxPaths[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_ServerRoot_IncludesAdminIni()
    {
        Touch("Admin.ini");
        MkDir("Worlds", "WorldA");
        Touch("Worlds", "WorldA", "SandboxSettings.ini");

        var results = AbioticIniCatalog.Discover(Path.Combine(_root, "Worlds", "WorldA"));

        Assert.Single(results, f => f.Kind == AbioticIniKind.ServerAdmin);
    }

    [Fact]
    public void Discover_ClientWorld_FindsSandboxSettingsOnlyForLoadedWorld()
    {
        // Simulate two client worlds sharing no Admin.ini
        // Worlds/
        //   Cascade/  SandboxSettings.ini
        //   df/       SandboxSettings.ini  <-- loaded
        MkDir("Worlds", "Cascade");
        Touch("Worlds", "Cascade", "SandboxSettings.ini");
        MkDir("Worlds", "df");
        Touch("Worlds", "df", "SandboxSettings.ini");

        var results = AbioticIniCatalog.Discover(Path.Combine(_root, "Worlds", "df"));

        var sandboxPaths = results
            .Where(f => f.Kind == AbioticIniKind.SandboxSettings)
            .Select(f => f.FullPath)
            .ToList();

        Assert.Single(sandboxPaths);
        Assert.Contains("df", sandboxPaths[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_ClientConfig_PicksUpWindowsIniFiles()
    {
        // Saved/
        //   Config/Windows/*.ini
        //   SaveGames/<id>/Worlds/MyWorld/
        MkDir("Saved", "Config", "Windows");
        Touch("Saved", "Config", "Windows", "Engine.ini");
        Touch("Saved", "Config", "Windows", "GameUserSettings.ini");
        MkDir("Saved", "SaveGames", "76561197993781479", "Worlds", "MyWorld");

        var results = AbioticIniCatalog.Discover(
            Path.Combine(_root, "Saved", "SaveGames", "76561197993781479", "Worlds", "MyWorld"));

        var clientFiles = results.Where(f => f.Kind == AbioticIniKind.ClientConfig).ToList();
        Assert.Equal(2, clientFiles.Count);
    }
}
