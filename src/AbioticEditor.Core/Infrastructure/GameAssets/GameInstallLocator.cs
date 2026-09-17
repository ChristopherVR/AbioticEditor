namespace AbioticEditor.Core.Assets;

/// <summary>Where a game install came from. Decides which <c>Binaries</c> folder the game's
/// executable (and so a mod loader) lives in when the folder itself gives no clue.</summary>
public enum GameInstallKind
{
    /// <summary>A Steam library copy: <c>&lt;library&gt;/steamapps/common/AbioticFactor</c>.</summary>
    Steam,

    /// <summary>A Game Pass / Microsoft Store copy installed by the Xbox app:
    /// <c>&lt;drive&gt;:\XboxGames\Abiotic Factor\Content</c>. Built with the WinGDK toolchain, so
    /// the executable sits in <c>Binaries\WinGDK</c>, not <c>Binaries\Win64</c>.</summary>
    GamePass,

    /// <summary>A folder the player pointed at by hand that matches neither layout above.</summary>
    Custom,
}

/// <summary>
/// One usable copy of the game on this machine.
/// </summary>
/// <param name="Root">The folder as detected or chosen (the install root, the inner
/// <c>AbioticFactor</c> folder or the <c>Paks</c> folder - any shape
/// <see cref="AfInstallLocator.ResolvePaksDirectory"/> accepts).</param>
/// <param name="Kind">Where it came from.</param>
/// <param name="PaksDirectory">The <c>Content/Paks</c> folder the catalogs read.</param>
/// <param name="BinariesDirectory">The folder holding the shipping executable, which is where a
/// mod loader (UE4SS) and this editor's own agent are installed: <c>Binaries\Win64</c> for a
/// Steam build, <c>Binaries\WinGDK</c> for a Game Pass build.</param>
public sealed record GameInstall(string Root, GameInstallKind Kind, string PaksDirectory, string BinariesDirectory)
{
    /// <summary>The <c>AbioticFactor</c> project folder that <c>Content</c> and <c>Binaries</c> sit in.</summary>
    public string ProjectRoot => Path.GetDirectoryName(Path.GetDirectoryName(PaksDirectory))!;

    /// <summary>True when the two installs are the same folder on disk, whatever shape each was given in.</summary>
    public bool IsSameInstallAs(GameInstall other)
        => string.Equals(Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(other.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Every copy of the game on this machine, with the folder layout each one uses. Built on
/// <see cref="AfInstallLocator"/>'s Steam and Game Pass detection (the same detectors save
/// discovery and the Game Data settings card rely on), this adds the one thing live editing needs
/// that the catalogs never did: which <c>Binaries</c> folder the executable is in. A Steam build
/// runs from <c>Binaries\Win64</c>; the Game Pass build is a WinGDK build and runs from
/// <c>Binaries\WinGDK</c>, which is also where UE4SS has to be installed for it (the community
/// UE4SS package for this game documents exactly that path for Game Pass copies).
/// </summary>
public static class GameInstallLocator
{
    private const string Win64 = "Win64";
    private const string WinGdk = "WinGDK";

    /// <summary>
    /// Every install detected on this machine: each Steam library copy, then each Game Pass copy,
    /// then the folder saved from Settings (or the new "This PC" step) when it is a different
    /// install from all of those. Duplicates (the saved folder pointing at a detected install)
    /// are folded together, so a saved path never shows up twice. Empty when nothing is found.
    /// </summary>
    public static IReadOnlyList<GameInstall> FindAll()
    {
        var roots = new List<(string Root, GameInstallKind Kind)>();
        foreach (var steam in AfInstallLocator.FindSteamInstallRoots()) roots.Add((steam, GameInstallKind.Steam));
        foreach (var gamePass in AfInstallLocator.FindGamePassInstallRoots()) roots.Add((gamePass, GameInstallKind.GamePass));
        foreach (var configured in new[] { Environment.GetEnvironmentVariable(AfInstallLocator.GameDirEnvVar), GamePathStore.Saved })
        {
            if (!string.IsNullOrWhiteSpace(configured)) roots.Add((configured, GameInstallKind.Custom));
        }
        return FindAll(roots);
    }

    /// <summary>The detection above over an explicit candidate list, so the folding and the
    /// layout rules can be exercised on temporary folders. Unusable roots are dropped.</summary>
    public static IReadOnlyList<GameInstall> FindAll(IEnumerable<(string Root, GameInstallKind Kind)> candidates)
    {
        var result = new List<GameInstall>();
        foreach (var (root, kind) in candidates)
        {
            var install = Resolve(root, kind);
            if (install is null || result.Any(existing => existing.IsSameInstallAs(install))) continue;
            result.Add(install);
        }
        return result;
    }

    /// <summary>
    /// The install live editing should use when the player has not just picked one: the folder
    /// saved in Settings when it still points at a game, otherwise the first detected copy.
    /// Null when there is no game on this machine at all.
    /// </summary>
    public static GameInstall? FindConfigured()
    {
        foreach (var configured in new[] { AfInstallLocator.OverrideInstallRoot, Environment.GetEnvironmentVariable(AfInstallLocator.GameDirEnvVar), GamePathStore.Saved })
        {
            if (!string.IsNullOrWhiteSpace(configured) && Resolve(configured) is { } install) return install;
        }
        var all = FindAll();
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// Describes the install at <paramref name="path"/>, or null when the folder is not a game
    /// install at all (no <c>Content/Paks</c> in any accepted shape). The kind is taken from
    /// <paramref name="kindHint"/> when the caller knows it (a detector), otherwise read off the
    /// path: a folder under <c>XboxGames</c> is a Game Pass copy, one under <c>steamapps</c> a
    /// Steam copy, anything else custom. The executable folder is then the one that actually
    /// holds a shipping executable; failing that, the one that exists; failing that, the one
    /// the kind implies - so a Game Pass copy always ends up on <c>WinGDK</c> even before UE4SS
    /// has ever been installed there.
    /// </summary>
    public static GameInstall? Resolve(string? path, GameInstallKind? kindHint = null)
    {
        var paks = AfInstallLocator.ResolvePaksDirectory(path);
        if (paks is null || string.IsNullOrWhiteSpace(path)) return null;
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(paks));
        if (string.IsNullOrEmpty(projectRoot)) return null;

        // A "custom" hint only says "the player typed this"; the path itself may still show
        // which store it came from.
        var kind = kindHint is null or GameInstallKind.Custom ? InferKind(paks) : kindHint.Value;
        var binaries = Path.Combine(projectRoot, "Binaries");
        var win64 = Path.Combine(binaries, Win64);
        var winGdk = Path.Combine(binaries, WinGdk);
        var preferred = kind == GameInstallKind.GamePass ? winGdk : win64;
        var other = kind == GameInstallKind.GamePass ? win64 : winGdk;

        string chosen;
        if (HasShippingExecutable(preferred)) chosen = preferred;
        else if (HasShippingExecutable(other)) chosen = other;
        else if (Directory.Exists(preferred)) chosen = preferred;
        else if (Directory.Exists(other)) chosen = other;
        else chosen = preferred;

        // A folder whose executable turned out to be the WinGDK build is a Game Pass copy whatever
        // its path looked like (a player who moved or symlinked one), and the other way round.
        if (chosen == winGdk && kind != GameInstallKind.GamePass && HasShippingExecutable(winGdk)) kind = GameInstallKind.GamePass;
        else if (chosen == win64 && kind == GameInstallKind.GamePass && HasShippingExecutable(win64)) kind = GameInstallKind.Custom;

        return new GameInstall(path, kind, paks, chosen);
    }

    /// <summary>
    /// Whether files can be created in <paramref name="directory"/> right now. The Xbox app keeps
    /// a Game Pass game's folder locked unless the player has turned on mods for that game, and
    /// a failed write only ever surfaced as a bare "access denied" - checking first lets setup
    /// explain what to do in plain words instead. Never throws; a missing folder counts as not
    /// writable.
    /// </summary>
    public static bool CanWriteTo(string directory)
    {
        if (!Directory.Exists(directory)) return false;
        var probe = Path.Combine(directory, ".abiotic-write-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static GameInstallKind InferKind(string paks)
    {
        foreach (var part in paks.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Equals("XboxGames", StringComparison.OrdinalIgnoreCase)) return GameInstallKind.GamePass;
            if (part.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) return GameInstallKind.Steam;
        }
        return GameInstallKind.Custom;
    }

    private static bool HasShippingExecutable(string directory)
    {
        try
        {
            return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*-Shipping.exe").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
