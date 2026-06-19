using System.IO;
using AbioticEditor.Core;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Tests;

internal static class Fixtures
{
    static Fixtures()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    public static string? CascadeDir { get; } = LocateCascade();

    /// <summary>
    /// The client save tree fixture: <c>dotnet/tests/fixtures/ClientSaved/SaveGames</c>
    /// (a copy of the game's own <c>Saved/SaveGames</c> folder, a NEWER game version than
    /// <see cref="CascadeDir"/>). Legacy fallback: <c>&lt;repo&gt;/Saved/SaveGames</c>.
    /// Null when absent so tests can skip gracefully.
    /// </summary>
    public static string? ClientSavedDir { get; } = LocateClientSaved();

    /// <summary>
    /// The dedicated-server world fixture: <c>dotnet/tests/fixtures/Server/Worlds/Cascade</c>.
    /// Legacy fallback: <c>&lt;repo&gt;/&lt;guid&gt;/SaveGames/Server/Worlds/Cascade</c>.
    /// Null when absent so tests can skip gracefully. The server root (Admin.ini, Backups)
    /// is two levels up from this directory.
    /// </summary>
    public static string? ServerWorldsDir { get; } = LocateServerWorlds();

    /// <summary>
    /// A sanitized Game Pass / Xbox "wgs" container fixture
    /// (<c>tests/fixtures/GamePass/&lt;account&gt;/</c>, contains <c>containers.index</c>).
    /// Null when absent so tests can skip gracefully.
    /// </summary>
    public static string? GamePassWgsDir { get; } = LocateGamePassWgs();

    private static string? LocateGamePassWgs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var root in new[]
                     {
                         Path.Combine(dir.FullName, "tests", "fixtures", "GamePass"),
                         Path.Combine(dir.FullName, "fixtures", "GamePass"),
                     })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var account in Directory.EnumerateDirectories(root))
                {
                    if (File.Exists(Path.Combine(account, "containers.index"))) return account;
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string? LocateCascade()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            // Canonical location: <repo>/dotnet/tests/fixtures/Cascade (checked via the
            // intermediate forms so the walk-up finds it from any depth), with the legacy
            // <repo>/Cascade root location kept as a fallback.
            string[] candidates =
            {
                Path.Combine(dir.FullName, "dotnet", "tests", "fixtures", "Cascade"),
                Path.Combine(dir.FullName, "tests", "fixtures", "Cascade"),
                Path.Combine(dir.FullName, "fixtures", "Cascade"),
                Path.Combine(dir.FullName, "Cascade"),
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "WorldSave_MetaData.sav")))
                {
                    return candidate;
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string? LocateClientSaved()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string[] candidates =
            {
                Path.Combine(dir.FullName, "dotnet", "tests", "fixtures", "ClientSaved", "SaveGames"),
                Path.Combine(dir.FullName, "tests", "fixtures", "ClientSaved", "SaveGames"),
                Path.Combine(dir.FullName, "fixtures", "ClientSaved", "SaveGames"),
                // Legacy location: the raw game tree copied to the repo root.
                Path.Combine(dir.FullName, "Saved", "SaveGames"),
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate)
                    && Directory.EnumerateFiles(candidate, "*.sav", SearchOption.AllDirectories).Any())
                {
                    return candidate;
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string? LocateServerWorlds()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string[] candidates =
            {
                Path.Combine(dir.FullName, "dotnet", "tests", "fixtures", "Server", "Worlds", "Cascade"),
                Path.Combine(dir.FullName, "tests", "fixtures", "Server", "Worlds", "Cascade"),
                Path.Combine(dir.FullName, "fixtures", "Server", "Worlds", "Cascade"),
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate)) return candidate;
            }

            // Legacy location: a GUID-named dedicated-server root at the repo root
            // (<dir>/SaveGames/Server/Worlds/Cascade or <dir>/<guid>/SaveGames/Server/Worlds/Cascade).
            try
            {
                var direct = Path.Combine(dir.FullName, "SaveGames", "Server", "Worlds", "Cascade");
                if (Directory.Exists(direct)) return direct;

                foreach (var child in Directory.EnumerateDirectories(dir.FullName))
                {
                    var candidate = Path.Combine(child, "SaveGames", "Server", "Worlds", "Cascade");
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Walking up may reach directories we can't enumerate; keep climbing.
            }
            dir = dir.Parent;
        }
        return null;
    }
}
