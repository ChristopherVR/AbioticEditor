using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

/// <summary>
/// The folder layout rules live editing relies on: a Steam copy runs from <c>Binaries\Win64</c>,
/// a Game Pass (WinGDK) copy from <c>Binaries\WinGDK</c>, and a mod loader has to be installed
/// into whichever one the executable is actually in. Exercised on temporary folders since only
/// one store's copy is ever installed on a given machine.
/// </summary>
public sealed class GameInstallLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "abiotic-install-layout-" + Guid.NewGuid().ToString("N"));

    /// <summary>Builds <c>&lt;installRoot&gt;/AbioticFactor/Content/Paks</c> plus the given
    /// Binaries subfolders, dropping a shipping executable into each of <paramref name="withExe"/>.</summary>
    private static string MakeInstall(string installRoot, string[] binariesFolders, params string[] withExe)
    {
        var project = Path.Combine(installRoot, "AbioticFactor");
        Directory.CreateDirectory(Path.Combine(project, "Content", "Paks"));
        File.WriteAllText(Path.Combine(project, "Content", "Paks", "pakchunk0-Windows.pak"), "fixture");
        foreach (var folder in binariesFolders)
        {
            Directory.CreateDirectory(Path.Combine(project, "Binaries", folder));
            if (withExe.Contains(folder))
                File.WriteAllText(Path.Combine(project, "Binaries", folder, $"AbioticFactor-{folder}-Shipping.exe"), "fixture");
        }
        return installRoot;
    }

    [Fact]
    public void A_steam_copy_runs_from_win64()
    {
        var root = MakeInstall(Path.Combine(_root, "steamapps", "common", "AbioticFactor"), ["Win64"], "Win64");
        var install = GameInstallLocator.Resolve(root);
        Assert.NotNull(install);
        Assert.Equal(GameInstallKind.Steam, install.Kind);
        Assert.Equal(Path.Combine(root, "AbioticFactor", "Binaries", "Win64"), install.BinariesDirectory);
        Assert.Equal(Path.Combine(root, "AbioticFactor", "Content", "Paks"), install.PaksDirectory);
    }

    [Fact]
    public void A_game_pass_copy_runs_from_wingdk()
    {
        var root = MakeInstall(Path.Combine(_root, "XboxGames", "Abiotic Factor", "Content"), ["WinGDK"], "WinGDK");
        var install = GameInstallLocator.Resolve(root);
        Assert.NotNull(install);
        Assert.Equal(GameInstallKind.GamePass, install.Kind);
        Assert.Equal(Path.Combine(root, "AbioticFactor", "Binaries", "WinGDK"), install.BinariesDirectory);
    }

    [Fact]
    public void A_game_pass_copy_with_no_binaries_folder_yet_still_targets_wingdk()
    {
        var root = MakeInstall(Path.Combine(_root, "somewhere", "Content"), []);
        var install = GameInstallLocator.Resolve(root, GameInstallKind.GamePass);
        Assert.NotNull(install);
        Assert.Equal(Path.Combine(root, "AbioticFactor", "Binaries", "WinGDK"), install.BinariesDirectory);
    }

    [Fact]
    public void The_folder_holding_the_executable_wins_over_the_path_shape()
    {
        // A copy the player moved out of XboxGames by hand: still a WinGDK build, so still a
        // Game Pass copy that needs its loader in WinGDK, whatever the path says.
        var root = MakeInstall(Path.Combine(_root, "Games", "AF"), ["Win64", "WinGDK"], "WinGDK");
        var install = GameInstallLocator.Resolve(root);
        Assert.NotNull(install);
        Assert.Equal(GameInstallKind.GamePass, install.Kind);
        Assert.EndsWith("WinGDK", install.BinariesDirectory);
    }

    [Fact]
    public void A_hand_picked_folder_without_store_clues_is_custom_and_uses_win64()
    {
        var root = MakeInstall(Path.Combine(_root, "Games", "AF"), ["Win64"]);
        var install = GameInstallLocator.Resolve(root);
        Assert.NotNull(install);
        Assert.Equal(GameInstallKind.Custom, install.Kind);
        Assert.EndsWith("Win64", install.BinariesDirectory);
    }

    [Fact]
    public void A_folder_that_is_not_the_game_is_rejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        Assert.Null(GameInstallLocator.Resolve(Path.Combine(_root, "empty")));
        Assert.Null(GameInstallLocator.Resolve(null));
    }

    [Fact]
    public void The_same_install_given_in_two_shapes_is_listed_once()
    {
        var steam = MakeInstall(Path.Combine(_root, "steamapps", "common", "AbioticFactor"), ["Win64"], "Win64");
        var gamePass = MakeInstall(Path.Combine(_root, "XboxGames", "Abiotic Factor", "Content"), ["WinGDK"], "WinGDK");
        var installs = GameInstallLocator.FindAll(
        [
            (steam, GameInstallKind.Steam),
            (gamePass, GameInstallKind.GamePass),
            // The saved Settings folder pointing at the inner project folder of the Steam copy.
            (Path.Combine(steam, "AbioticFactor"), GameInstallKind.Custom),
            (Path.Combine(_root, "nowhere"), GameInstallKind.Custom),
        ]);
        Assert.Equal(2, installs.Count);
        Assert.Equal(GameInstallKind.Steam, installs[0].Kind);
        Assert.Equal(GameInstallKind.GamePass, installs[1].Kind);
        Assert.True(installs[0].IsSameInstallAs(GameInstallLocator.Resolve(Path.Combine(steam, "AbioticFactor"))!));
    }

    [Fact]
    public void Writability_is_checked_without_leaving_anything_behind()
    {
        var folder = Path.Combine(_root, "writable");
        Directory.CreateDirectory(folder);
        Assert.True(GameInstallLocator.CanWriteTo(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.False(GameInstallLocator.CanWriteTo(Path.Combine(_root, "missing")));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
