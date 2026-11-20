using System.Globalization;
using System.Text.Json;
using AbioticEditor.Core.Ini;

namespace AbioticEditor.Core.Assets;

/// <summary>Reads completed Abiotic Factor mod.io installations without changing subscriptions or game load order.</summary>
public static class ModIoInstallLocator
{
    public const long GameId = 13105;
    public const string RootEnvironmentVariable = "ABIOTIC_MODIO_ROOT";
    public const string KeyPrefix = "mod.io/";

    /// <summary>Overrides the folder holding the game's <c>GameUserSettings.ini</c> (tests, unusual installs).</summary>
    public const string GameConfigEnvironmentVariable = "ABIOTIC_GAME_CONFIG_DIR";

    // The SDK's persisted ModState values (modio-sdk ModCollectionEntry): pending, installed,
    // update pending, downloading, extracting, uninstall pending. An update-pending mod still has
    // its previous version on disk, which is what the game loads until the update lands.
    private const int StatePending = 0, StateInstalled = 1, StateUpdatePending = 2, StateDownloading = 3, StateExtracting = 4;

    /// <summary>A mod the player subscribed to that has not finished downloading yet.</summary>
    public sealed record PendingMod(long ModIoId, string DisplayName);

    public static IReadOnlyList<AfInstallLocator.InstalledMod> FindInstalled()
        => StorageRoots().SelectMany(FindInstalledAt).DistinctBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The <see cref="KeyPrefix"/> mod key for a mod.io id (how the editor remembers per-mod choices).</summary>
    public static string KeyFor(long modId) => KeyPrefix + modId.ToString(CultureInfo.InvariantCulture);

    /// <param name="storageRoot">The SDK storage root containing the 13105 game directory.</param>
    public static IReadOnlyList<AfInstallLocator.InstalledMod> FindInstalledAt(string storageRoot)
    {
        var result = new List<AfInstallLocator.InstalledMod>();
        try
        {
            var gameRoot = GameRoot(storageRoot);
            var statePath = Path.Combine(gameRoot, "metadata", "state.json");
            if (!File.Exists(statePath))
            {
                // No index (an older SDK layout, or a copied cache): the extracted mods/<id>
                // folders are the installs. A folder needs a mod archive to count.
                foreach (var (modId, directory) in ModFolders(gameRoot))
                    if (Installed(gameRoot, modId, directory, null) is { } mod) result.Add(mod);
                return result;
            }
            foreach (var entry in ReadState(statePath))
            {
                if (entry.State is not (StateInstalled or StateUpdatePending) || entry.Path is not { } recorded) continue;
                // The game loads each mod from the folder the index records, even when that is
                // outside today's storage folder (RootLocalStoragePath was changed and the SDK kept
                // the earlier installs where they were), so follow it wherever it points.
                if (HostPath(recorded, storageRoot) is not { } directory || !Path.IsPathFullyQualified(directory)) continue;
                if (InstalledFromIndex(entry.Id, Path.GetFullPath(directory), entry.Name) is { } mod) result.Add(mod);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Diagnostics.EditorLog.Warn("Mods", "Could not read the local Abiotic Factor mod.io installation index.", ex);
        }
        return result;
    }

    /// <summary>
    /// Mods the player subscribed to (or the game queued) whose files are not on disk yet. Starting
    /// the game, or loading a world that uses them, makes the game download them. Names come from
    /// the SDK index when it has them.
    /// </summary>
    public static IReadOnlyList<PendingMod> FindPending()
    {
        var installed = FindInstalled().Select(mod => mod.ModIoId).OfType<long>().ToHashSet();
        var roots = StorageRoots().ToArray();
        return roots.SelectMany(root => FindPendingAt(root, ReadSubscriptions()))
            .Where(mod => !installed.Contains(mod.ModIoId)).DistinctBy(mod => mod.ModIoId).ToArray();
    }

    /// <param name="storageRoot">The SDK storage root containing the 13105 game directory.</param>
    /// <param name="subscriptions">The signed-in user's subscribed mod ids.</param>
    public static IReadOnlyList<PendingMod> FindPendingAt(string storageRoot, IReadOnlyCollection<long> subscriptions)
    {
        var result = new Dictionary<long, PendingMod>();
        var installed = FindInstalledAt(storageRoot).Select(mod => mod.ModIoId).OfType<long>().ToHashSet();
        var names = new Dictionary<long, string>();
        try
        {
            var statePath = Path.Combine(GameRoot(storageRoot), "metadata", "state.json");
            if (File.Exists(statePath))
            {
                foreach (var entry in ReadState(statePath))
                {
                    if (entry.Name is { } name) names[entry.Id] = name;
                    if (entry.State is StatePending or StateDownloading or StateExtracting && !installed.Contains(entry.Id))
                        result[entry.Id] = new(entry.Id, entry.Name ?? DefaultName(entry.Id));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Diagnostics.EditorLog.Warn("Mods", "Could not read the local Abiotic Factor mod.io installation index.", ex);
        }
        foreach (var id in subscriptions)
            if (!installed.Contains(id)) result.TryAdd(id, new(id, names.GetValueOrDefault(id) ?? DefaultName(id)));
        return result.Values.OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// The mod ids the signed-in mod.io user(s) on this PC subscribed to, from the SDK's
    /// per-user <c>user.json</c>. Login tokens in the same file are never read.
    /// </summary>
    public static IReadOnlyList<long> ReadSubscriptions()
    {
        var ids = new HashSet<long>();
        foreach (var user in UserSettingsFiles())
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(user));
                if (json.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in json.RootElement.EnumerateObject())
                {
                    if (!property.Name.Equals("subscriptions", StringComparison.OrdinalIgnoreCase)
                        || property.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var item in property.Value.EnumerateArray())
                        if (ReadId(item) is { } id) ids.Add(id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return ids.Order().ToArray();
    }

    /// <summary>
    /// The mods the game itself has switched on (<c>EnabledMods</c> in its
    /// <c>GameUserSettings.ini</c>), or null when no game config was found. The editor follows
    /// this until the player makes their own choice for a mod.
    /// </summary>
    public static IReadOnlySet<long>? ReadGameEnabledMods()
    {
        IReadOnlySet<long>? result = null;
        foreach (var path in GameUserSettingsFiles())
        {
            var ids = ReadGameEnabledModsFrom(path);
            if (ids is null) continue;
            result = result is null ? ids : result.Union(ids).ToHashSet();
        }
        return result;
    }

    /// <summary>Reads <c>EnabledMods</c> from one <c>GameUserSettings.ini</c>; null when the file cannot be read.</summary>
    public static IReadOnlySet<long>? ReadGameEnabledModsFrom(string gameUserSettingsPath)
    {
        try
        {
            if (!File.Exists(gameUserSettingsPath)) return null;
            var section = IniFile.Load(gameUserSettingsPath).FindSection("/Script/AbioticFactor.AbioticGameUserSettings");
            var ids = new HashSet<long>();
            if (section is null) return ids;
            // UE writes config arrays as repeated keys (optionally +/. prefixed), and older
            // builds as one parenthesized list; accept both.
            foreach (var (key, value) in section.Entries)
            {
                if (!key.TrimStart('+', '.').Equals("EnabledMods", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var part in value.Trim().TrimStart('(').TrimEnd(')').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0) ids.Add(id);
            }
            return ids;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private sealed record StateEntry(long Id, int State, string? Path, string? Name);

    private static List<StateEntry> ReadState(string statePath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(statePath));
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty("Mods", out var entries) || entries.ValueKind != JsonValueKind.Array) return [];
        var result = new List<StateEntry>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("State", out var state) || state.ValueKind != JsonValueKind.Number || !state.TryGetInt32(out var stateValue)
                || !entry.TryGetProperty("ID", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var modId) || modId <= 0) continue;
            string? name = null;
            if (entry.TryGetProperty("Profile", out var profile) && profile.ValueKind == JsonValueKind.Object)
            {
                if (profile.TryGetProperty("game_id", out var game) && (game.ValueKind != JsonValueKind.Number || !game.TryGetInt64(out var gameId) || gameId != GameId)) continue;
                if (profile.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(named.GetString()))
                    name = named.GetString();
            }
            var path = entry.TryGetProperty("PathOnDisk", out var onDisk) && onDisk.ValueKind == JsonValueKind.String ? onDisk.GetString() : null;
            result.Add(new(modId, stateValue, string.IsNullOrWhiteSpace(path) ? null : path, name));
        }
        return result;
    }

    /// <summary>A folder found by scanning <c>mods/&lt;id&gt;</c>: it must really sit inside the game's cache.</summary>
    private static AfInstallLocator.InstalledMod? Installed(string gameRoot, long modId, string directory, string? name)
    {
        if (!directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(directory) || HasReparsePoint(gameRoot, directory)) return null;
        return ModAt(modId, directory, name);
    }

    /// <summary>A folder the SDK's own index names: used wherever it is, because the game uses it.</summary>
    private static AfInstallLocator.InstalledMod? InstalledFromIndex(long modId, string directory, string? name)
        => Directory.Exists(directory) ? ModAt(modId, directory, name) : null;

    private static AfInstallLocator.InstalledMod? ModAt(long modId, string directory, string? name)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = Directory.EnumerateFiles(directory, "*", options)
            .Where(file => Path.GetExtension(file).Equals(".pak", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(file).Equals(".utoc", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(file, ".ucas")))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) return null;
        return new(KeyFor(modId), files) { DisplayName = name ?? PluginName(directory) ?? DefaultName(modId), ModIoId = modId };
    }

    /// <summary>The <c>FriendlyName</c> of the <c>.uplugin</c> every official mod archive carries at its root.</summary>
    private static string? PluginName(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.uplugin"))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("FriendlyName", out var name)
                    && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } value && !IsTemplateName(value)) return value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    /// <summary>
    /// True for the placeholder plugin names the official mod editor starts every project with
    /// ("MyProject", "MyProject_Test", ...). Many published mods keep them, so two unrelated mods
    /// can share one; such a name says nothing about the mod and is not shown.
    /// </summary>
    internal static bool IsTemplateName(string name)
        => name.StartsWith("MyProject", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("NewPlugin", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("NewMod", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(long Id, string Directory)> ModFolders(string gameRoot)
    {
        var mods = Path.Combine(gameRoot, "mods");
        if (!Directory.Exists(mods)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(mods))
            if (long.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                yield return (id, Path.GetFullPath(directory));
    }

    private static string GameRoot(string storageRoot)
        => Path.GetFullPath(Path.Combine(storageRoot, GameId.ToString(CultureInfo.InvariantCulture)));

    private static string DefaultName(long modId) => $"mod.io #{modId}";

    private static long? ReadId(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id) && id > 0) return id;
        if (item.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "id", "ID", "ModId" })
                if (item.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.Number && nested.TryGetInt64(out var value) && value > 0) return value;
        return null;
    }

    private static bool HasReparsePoint(string root, string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Each place the SDK keeps its settings (<c>%LOCALAPPDATA%\mod.io</c>, inside the Proton prefix
    /// off Windows), with that install's default mod storage folder (<c>Public\mod.io</c>).
    /// </summary>
    private static IEnumerable<(string Settings, string? DefaultRoot)> SettingsLocations()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mod.io"),
                Environment.GetEnvironmentVariable("PUBLIC") is { Length: > 0 } shared ? Path.Combine(shared, "mod.io") : null);
            yield break;
        }
        foreach (var library in AfInstallLocator.FindSteamLibraryRoots())
        {
            var driveC = ProtonDriveC(library);
            yield return (Path.Combine(driveC, "users", "steamuser", "AppData", "Local", "mod.io"), Path.Combine(driveC, "users", "Public", "mod.io"));
        }
    }

    private static IEnumerable<string> UserSettingsFiles() => SettingsLocations().SelectMany(location => UserSettingsFilesIn(location.Settings));

    private static IEnumerable<string> UserSettingsFilesIn(string settingsRoot)
    {
        var gameSettings = Path.Combine(settingsRoot, GameId.ToString(CultureInfo.InvariantCulture));
        string[] users;
        try { users = Directory.Exists(gameSettings) ? Directory.GetDirectories(gameSettings) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { users = []; }
        foreach (var user in users)
        {
            var file = Path.Combine(user, "user.json");
            if (File.Exists(file)) yield return file;
        }
    }

    /// <summary>Where the game keeps the <c>GameUserSettings.ini</c> holding its <c>EnabledMods</c>.</summary>
    public static IEnumerable<string> GameUserSettingsPaths() => GameUserSettingsFiles();

    private static IEnumerable<string> GameUserSettingsFiles()
    {
        if (Environment.GetEnvironmentVariable(GameConfigEnvironmentVariable) is { Length: > 0 } configured)
        { yield return Path.Combine(configured, "GameUserSettings.ini"); yield break; }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // Steam writes Windows/, the Game Pass build WinGDK/.
        foreach (var platform in new[] { "Windows", "WinGDK" })
            yield return Path.Combine(local, "AbioticFactor", "Saved", "Config", platform, "GameUserSettings.ini");
        if (!OperatingSystem.IsWindows())
            foreach (var library in AfInstallLocator.FindSteamLibraryRoots())
                yield return Path.Combine(ProtonDriveC(library), "users", "steamuser",
                    "AppData", "Local", "AbioticFactor", "Saved", "Config", "Windows", "GameUserSettings.ini");
    }

    /// <summary>
    /// Where the game's mod.io files live, following the SDK's rules: a per-game
    /// <c>RootLocalStoragePath</c> in a player's <c>user.json</c> wins, then the one in
    /// <c>globalsettings.json</c>, then the default shared <c>Public\mod.io</c> folder. Under
    /// Proton all of this sits in the game's Wine prefix.
    /// </summary>
    private static IEnumerable<string> StorageRoots()
    {
        if (Environment.GetEnvironmentVariable(RootEnvironmentVariable) is { Length: > 0 } configured)
        { yield return configured; yield break; }
        foreach (var (settings, defaultRoot) in SettingsLocations())
            foreach (var root in StorageRootsFor(settings, defaultRoot)) yield return root;
    }

    /// <summary>The storage folder(s) one SDK settings folder selects (see <see cref="StorageRoots"/>).</summary>
    internal static IReadOnlyList<string> StorageRootsFor(string settingsRoot, string? defaultRoot)
    {
        var userRoots = UserSettingsFilesIn(settingsRoot).Select(ReadRoot).OfType<string>()
            .Select(root => HostPath(root, settingsRoot)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (userRoots.Length > 0) return userRoots;
        if (ReadRoot(Path.Combine(settingsRoot, "globalsettings.json")) is { } global && HostPath(global, settingsRoot) is { } mapped) return [mapped];
        return defaultRoot is null ? [] : [defaultRoot];
    }

    private static string? ReadRoot(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("RootLocalStoragePath", out var root) && root.ValueKind == JsonValueKind.String
                && root.GetString() is { Length: > 0 } value ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>
    /// Turns a path the game wrote into one this machine can open. Under Proton the game writes
    /// Windows paths (<c>D:\Mods</c>), which live in the Wine prefix's drive folders
    /// (<c>pfx/dosdevices/d:</c>); <paramref name="nearby"/> is any folder inside that prefix.
    /// Null when such a path has no prefix to resolve against.
    /// </summary>
    internal static string? HostPath(string path, string nearby)
    {
        if (OperatingSystem.IsWindows() || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/'))
            return path;
        for (var directory = new DirectoryInfo(nearby); directory is not null; directory = directory.Parent)
        {
            var devices = Path.Combine(directory.FullName, "dosdevices");
            if (Directory.Exists(devices))
                return Path.Combine(devices, char.ToLowerInvariant(path[0]) + ":", path[3..].Replace('\\', '/'));
        }
        return null;
    }

    private static string ProtonDriveC(string library)
        => Path.Combine(library, "steamapps", "compatdata", "427410", "pfx", "drive_c");
}
