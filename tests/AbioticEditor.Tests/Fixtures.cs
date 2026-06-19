using System.IO;
using AbioticEditor.Core;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Tests;

/// <summary>
/// Locates the on-disk save fixtures, which are grouped by platform under
/// <c>tests/fixtures/</c>:
/// <list type="bullet">
///   <item><c>SteamSaves/</c> - the Steam tree. <c>Config/Windows/</c> + <c>SaveGames/</c> mirror
///     the game's real <c>Saved/</c> install (see <see cref="ClientSavedDir"/>), and
///     <c>Legacy/Cascade/</c> keeps an older-version standalone world (see <see cref="CascadeDir"/>)
///     used as a cross-version control.</item>
///   <item><c>GamePassSaves/</c> - a sanitized Xbox "wgs" container (see <see cref="GamePassWgsDir"/>).</item>
///   <item><c>DedicatedServerSaves/</c> - a dedicated-server root with <c>Admin.ini</c> and
///     <c>Worlds/Cascade/</c> (see <see cref="ServerWorldsDir"/>).</item>
/// </list>
/// Backups are intentionally not checked in. Every accessor returns null when its fixture is
/// absent so tests can skip gracefully.
/// </summary>
internal static class Fixtures
{
    static Fixtures()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// The canonical primary fixture: the legacy standalone single-player world
    /// (<c>tests/fixtures/SteamSaves/Legacy/Cascade</c>), a complete world folder with
    /// <c>PlayerData/</c>, <c>WorldSave_*.sav</c> and <c>SandboxSettings.ini</c>. This is an
    /// OLDER game version than <see cref="ClientSavedDir"/>. Null when absent so tests skip.
    /// </summary>
    public static string? CascadeDir { get; } = LocateCascade();

    /// <summary>
    /// The Steam client SaveGames root: <c>tests/fixtures/SteamSaves/SaveGames</c> (a copy of the
    /// game's own <c>Saved/SaveGames</c> folder, a NEWER game version than <see cref="CascadeDir"/>).
    /// Contains <c>&lt;steamid&gt;/Worlds/&lt;World&gt;</c> so discovery can resolve the account id
    /// and platform. Null when absent so tests can skip gracefully.
    /// </summary>
    public static string? ClientSavedDir { get; } = LocateClientSaved();

    /// <summary>
    /// The dedicated-server world fixture: <c>tests/fixtures/DedicatedServerSaves/Worlds/Cascade</c>.
    /// Null when absent so tests can skip gracefully. The server root (Admin.ini, Worlds) is two
    /// levels up from this directory.
    /// </summary>
    public static string? ServerWorldsDir { get; } = LocateServerWorlds();

    /// <summary>
    /// A sanitized Game Pass / Xbox "wgs" container fixture
    /// (<c>tests/fixtures/GamePassSaves/&lt;account&gt;/</c>, contains <c>containers.index</c>).
    /// Null when absent so tests can skip gracefully.
    /// </summary>
    public static string? GamePassWgsDir { get; } = LocateGamePassWgs();

    /// <summary>Walks up from the test binary looking for <c>tests/fixtures</c> (or <c>fixtures</c>).</summary>
    private static IEnumerable<string> FixtureRoots()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "tests", "fixtures");
            yield return Path.Combine(dir.FullName, "fixtures");
            dir = dir.Parent;
        }
    }

    private static string? LocateGamePassWgs()
    {
        foreach (var root in FixtureRoots())
        {
            // Canonical platform-grouped location, then the pre-regroup "GamePass" name.
            foreach (var groupDir in new[] { Path.Combine(root, "GamePassSaves"), Path.Combine(root, "GamePass") })
            {
                if (!Directory.Exists(groupDir)) continue;
                foreach (var account in Directory.EnumerateDirectories(groupDir))
                {
                    if (File.Exists(Path.Combine(account, "containers.index"))) return account;
                }
            }
        }
        return null;
    }

    private static string? LocateCascade()
    {
        foreach (var root in FixtureRoots())
        {
            // Canonical platform-grouped location, then the pre-regroup top-level "Cascade".
            foreach (var candidate in new[] { Path.Combine(root, "SteamSaves", "Legacy", "Cascade"), Path.Combine(root, "Cascade") })
            {
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "WorldSave_MetaData.sav")))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private static string? LocateClientSaved()
    {
        foreach (var root in FixtureRoots())
        {
            // Canonical platform-grouped location, then the pre-regroup "ClientSaved/SaveGames".
            foreach (var candidate in new[]
                     {
                         Path.Combine(root, "SteamSaves", "SaveGames"),
                         Path.Combine(root, "ClientSaved", "SaveGames"),
                     })
            {
                if (Directory.Exists(candidate)
                    && Directory.EnumerateFiles(candidate, "*.sav", SearchOption.AllDirectories).Any())
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private static string? LocateServerWorlds()
    {
        foreach (var root in FixtureRoots())
        {
            // Canonical platform-grouped location, then the pre-regroup "Server/Worlds/Cascade".
            foreach (var candidate in new[]
                     {
                         Path.Combine(root, "DedicatedServerSaves", "Worlds", "Cascade"),
                         Path.Combine(root, "Server", "Worlds", "Cascade"),
                     })
            {
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
