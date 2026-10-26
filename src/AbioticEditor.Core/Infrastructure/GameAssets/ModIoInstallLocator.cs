using System.Text.Json;

namespace AbioticEditor.Core.Assets;

/// <summary>Reads completed Abiotic Factor mod.io installations without changing subscriptions or game load order.</summary>
public static class ModIoInstallLocator
{
    public const long GameId = 13105;
    public const string RootEnvironmentVariable = "ABIOTIC_MODIO_ROOT";
    public const string KeyPrefix = "mod.io/";

    public static IReadOnlyList<AfInstallLocator.InstalledMod> FindInstalled()
        => StorageRoots().SelectMany(FindInstalledAt).DistinctBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <param name="storageRoot">The SDK storage root containing the 13105 game directory.</param>
    public static IReadOnlyList<AfInstallLocator.InstalledMod> FindInstalledAt(string storageRoot)
    {
        var result = new List<AfInstallLocator.InstalledMod>();
        try
        {
            var gameRoot = Path.GetFullPath(Path.Combine(storageRoot, GameId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var statePath = Path.Combine(gameRoot, "metadata", "state.json");
            if (!File.Exists(statePath)) return result;
            using var json = JsonDocument.Parse(File.ReadAllText(statePath));
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("Mods", out var entries) || entries.ValueKind != JsonValueKind.Array) return result;
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("State", out var state) || state.ValueKind != JsonValueKind.Number || !state.TryGetInt32(out var stateValue) || stateValue != 1
                    || !entry.TryGetProperty("ID", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var modId) || modId <= 0
                    || !entry.TryGetProperty("PathOnDisk", out var path) || path.ValueKind != JsonValueKind.String) continue;
                var directory = path.GetString();
                if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) continue;
                directory = Path.GetFullPath(directory);
                if (!directory.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !Directory.Exists(directory) || HasReparsePoint(gameRoot, directory)) continue;
                var display = $"mod.io #{modId}";
                if (entry.TryGetProperty("Profile", out var profile) && profile.ValueKind == JsonValueKind.Object)
                {
                    if (profile.TryGetProperty("game_id", out var game) && (game.ValueKind != JsonValueKind.Number || !game.TryGetInt64(out var gameId) || gameId != GameId)) continue;
                    if (profile.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                        display = name.GetString()!;
                }
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                var files = Directory.EnumerateFiles(directory, "*", options)
                    .Where(file => Path.GetExtension(file).Equals(".pak", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file).Equals(".utoc", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(file, ".ucas")))
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                if (files.Length > 0) result.Add(new(KeyPrefix + modId.ToString(System.Globalization.CultureInfo.InvariantCulture), files)
                { DisplayName = display, ModIoId = modId });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Diagnostics.EditorLog.Warn("Mods", "Could not read the local Abiotic Factor mod.io installation index.", ex);
        }
        return result;
    }

    private static bool HasReparsePoint(string root, string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (string.Equals(directory.FullName, root, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static IEnumerable<string> StorageRoots()
    {
        if (Environment.GetEnvironmentVariable(RootEnvironmentVariable) is { Length: > 0 } configured)
        { yield return configured; yield break; }
        var settingsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mod.io");
        var global = ReadRoot(Path.Combine(settingsRoot, "globalsettings.json"));
        if (global is not null) yield return global;
        var gameSettings = Path.Combine(settingsRoot, GameId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string[] users;
        try { users = Directory.Exists(gameSettings) ? Directory.GetDirectories(gameSettings) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { users = []; }
        foreach (var user in users)
            if (ReadRoot(Path.Combine(user, "user.json")) is { } root) yield return root;
        if (OperatingSystem.IsWindows())
        {
            if (Environment.GetEnvironmentVariable("PUBLIC") is { Length: > 0 } shared) yield return Path.Combine(shared, "mod.io");
        }
        else
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "mod.io");
            foreach (var library in AfInstallLocator.FindSteamLibraryRoots())
                yield return Path.Combine(library, "steamapps", "compatdata", "427410", "pfx", "drive_c", "users", "Public", "mod.io");
        }
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
}
