namespace AbioticEditor.Core.Ini;

/// <summary>Which known Abiotic Factor ini a discovered file is.</summary>
public enum AbioticIniKind
{
    /// <summary>
    /// Dedicated-server <c>Admin.ini</c> at the server save root (the directory two
    /// levels up from <c>Worlds/&lt;World&gt;</c>). Sections <c>[Moderators]</c> and
    /// <c>[BannedPlayers]</c> hold one duplicate-keyed line per entry
    /// (<c>Moderator=&lt;steamid64&gt;</c> / <c>BannedPlayer=&lt;steamid64&gt;</c>).
    /// </summary>
    ServerAdmin,

    /// <summary>
    /// <c>SandboxSettings.ini</c> next to a world's <c>WorldSave_*.sav</c> files:
    /// a single <c>[SandboxSettings]</c> section of difficulty knobs (the game appends
    /// changed values as extra lines after the section, sometimes with different line
    /// endings; last write wins).
    /// </summary>
    SandboxSettings,

    /// <summary>
    /// Client config under <c>Saved/Config/Windows/</c> (<c>Engine.ini</c>,
    /// <c>GameUserSettings.ini</c>, <c>Input.ini</c>, <c>Settings.ini</c>, ...).
    /// </summary>
    ClientConfig,
}

/// <summary>One ini file found near a save tree.</summary>
public sealed record AbioticIniFile(string FullPath, AbioticIniKind Kind);

/// <summary>
/// Catalog of the ini files an Abiotic Factor save folder may carry, plus a discovery
/// helper that finds them near any save path. Layouts handled:
/// <list type="bullet">
/// <item>Dedicated server: <c>&lt;root&gt;/Admin.ini</c> + <c>&lt;root&gt;/Worlds/&lt;World&gt;/SandboxSettings.ini</c>
/// (the server root is two levels up from a world folder; <c>&lt;root&gt;/Backups</c> is ignored).</item>
/// <item>Client: <c>Saved/SaveGames/&lt;steamid&gt;/Worlds/&lt;World&gt;/...</c> with config
/// in the sibling <c>Saved/Config/Windows/*.ini</c>.</item>
/// </list>
/// </summary>
public static class AbioticIniCatalog
{
    /// <summary>File name of the dedicated-server moderator/ban list.</summary>
    public const string AdminFileName = "Admin.ini";

    /// <summary>File name of the per-world difficulty settings.</summary>
    public const string SandboxSettingsFileName = "SandboxSettings.ini";

    /// <summary>Admin.ini section listing moderators.</summary>
    public const string ModeratorsSection = "Moderators";

    /// <summary>Duplicate key inside <see cref="ModeratorsSection"/>; one steamid64 per line.</summary>
    public const string ModeratorKey = "Moderator";

    /// <summary>Admin.ini section listing banned players.</summary>
    public const string BannedPlayersSection = "BannedPlayers";

    /// <summary>Duplicate key inside <see cref="BannedPlayersSection"/>; one steamid64 per line.</summary>
    public const string BannedPlayerKey = "BannedPlayer";

    /// <summary>Short uppercase tag for an ini kind (e.g. "SANDBOX SETTINGS").</summary>
    public static string LabelFor(AbioticIniKind kind) => kind switch
    {
        AbioticIniKind.ServerAdmin => "SERVER ADMIN",
        AbioticIniKind.SandboxSettings => "SANDBOX SETTINGS",
        AbioticIniKind.ClientConfig => "CLIENT CONFIG",
        _ => "INI",
    };

    /// <summary>One-line explanation of what an ini kind controls and how the game treats it.</summary>
    public static string DescriptionFor(AbioticIniKind kind) => kind switch
    {
        AbioticIniKind.ServerAdmin =>
            "Dedicated-server moderator and ban lists. One steamid64 per line; the game reads this on boot.",
        AbioticIniKind.SandboxSettings =>
            "Per-world difficulty settings. The game appends changed values on save; the last occurrence of a key wins.",
        AbioticIniKind.ClientConfig =>
            "Client engine/game configuration. Edit with care: the game rewrites parts of these files on exit.",
        _ => string.Empty,
    };

    /// <summary>How many directory levels <see cref="Discover"/> walks up from the start.</summary>
    private const int MaxWalkUp = 6;

    /// <summary>
    /// Finds the known ini files near <paramref name="path"/> (a save file, a world
    /// folder, or any directory inside a save tree). Walks up a few levels checking each
    /// directory for <c>Admin.ini</c>, <c>SandboxSettings.ini</c> and a
    /// <c>Config/Windows</c> subtree.
    ///
    /// When <c>Admin.ini</c> is found (dedicated-server root), only the
    /// <c>SandboxSettings.ini</c> for the world that contains <paramref name="path"/> is
    /// included - not every world under the server. This prevents sandbox settings from
    /// sibling worlds leaking into the config panel for the currently loaded world.
    ///
    /// Results are de-duplicated and ordered by kind, then path.
    /// </summary>
    public static IReadOnlyList<AbioticIniFile> Discover(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var dir = File.Exists(path)
            ? Path.GetDirectoryName(Path.GetFullPath(path))
            : (Directory.Exists(path) ? Path.GetFullPath(path) : null);
        if (dir is null)
        {
            return [];
        }

        var found = new Dictionary<string, AbioticIniFile>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(dir);
        for (var depth = 0; current is not null && depth < MaxWalkUp; depth++, current = current.Parent)
        {
            ProbeDirectory(current.FullName, dir, found);
        }

        return found.Values
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ProbeDirectory(string dir, string startDir, Dictionary<string, AbioticIniFile> found)
    {
        var sandbox = Path.Combine(dir, SandboxSettingsFileName);
        if (File.Exists(sandbox))
        {
            Add(found, sandbox, AbioticIniKind.SandboxSettings);
        }

        var admin = Path.Combine(dir, AdminFileName);
        if (File.Exists(admin))
        {
            Add(found, admin, AbioticIniKind.ServerAdmin);

            // Server root: only include the SandboxSettings.ini for the world that contains
            // startDir - not every sibling world. Opening "df" must not pull in "Cascade"'s
            // settings even though both live under the same Worlds/ directory.
            var worlds = Path.Combine(dir, "Worlds");
            if (Directory.Exists(worlds))
            {
                foreach (var world in Directory.EnumerateDirectories(worlds))
                {
                    var fullWorld = Path.GetFullPath(world);
                    if (!startDir.StartsWith(fullWorld, StringComparison.OrdinalIgnoreCase)) continue;
                    var worldSandbox = Path.Combine(world, SandboxSettingsFileName);
                    if (File.Exists(worldSandbox))
                    {
                        Add(found, worldSandbox, AbioticIniKind.SandboxSettings);
                    }
                }
            }
        }

        // Client tree: <...>/Saved/Config/Windows/*.ini next to <...>/Saved/SaveGames.
        var configWindows = Path.Combine(dir, "Config", "Windows");
        if (Directory.Exists(configWindows))
        {
            foreach (var ini in Directory.EnumerateFiles(configWindows, "*.ini", SearchOption.TopDirectoryOnly))
            {
                Add(found, ini, AbioticIniKind.ClientConfig);
            }
        }
    }

    private static void Add(Dictionary<string, AbioticIniFile> found, string path, AbioticIniKind kind)
    {
        var full = Path.GetFullPath(path);
        if (!found.ContainsKey(full))
        {
            found.Add(full, new AbioticIniFile(full, kind));
        }
    }
}
