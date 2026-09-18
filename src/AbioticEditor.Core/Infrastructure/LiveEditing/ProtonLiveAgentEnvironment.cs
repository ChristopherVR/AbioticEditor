namespace AbioticEditor.Core.LiveEditing;

/// <summary>
/// Resolves the Steam Play (Proton) Wine prefix a Linux copy of this game runs in, and the
/// <c>%LOCALAPPDATA%</c>-equivalent folder inside it. Windows never needs this (the game and this
/// editor already share one real Windows profile). On Linux, the game is still the same Windows
/// binary - Proton runs it inside a per-app Wine prefix at
/// <c>&lt;steam library&gt;/steamapps/compatdata/&lt;appid&gt;/pfx</c>, and that prefix is where
/// the in-game UE4SS Lua mod's own <c>os.getenv("LOCALAPPDATA")</c> resolves to (see
/// <c>AbioticEditor.Core.Saves.SaveDiscovery.DiscoverProtonClientWorlds</c>, which already reads
/// the game's save tree from the exact same prefix layout). This editor's own native helper
/// process (<c>AbioticEditorLiveAgentHelper.exe</c>, still a Windows binary - see
/// <c>AbioticEditor.Web.Services.LiveAgentSetup</c>) has to be launched through that same prefix,
/// with a matching <c>LOCALAPPDATA</c>, for its token/port files and the request/response file
/// mailbox to land where the Lua mod can see them.
///
/// <para>Only used on Linux; on Windows and macOS every caller should skip straight past it.</para>
/// </summary>
public static class ProtonLiveAgentEnvironment
{
    /// <summary>The fixed default Windows account name Steam creates inside every Proton prefix
    /// it manages. Not something read from the prefix - Proton always uses this exact name, the
    /// same literal <c>AbioticEditor.Core.Saves.SaveDiscovery</c>'s own Proton save-path already
    /// relies on.</summary>
    private const string PrefixUser = "steamuser";

    /// <summary>The literal Windows-style <c>%LOCALAPPDATA%</c> value to hand a process this
    /// editor launches into a Proton prefix (the native helper), so it resolves to the exact same
    /// folder the game's own Proton-launched process sees for that same environment variable.
    /// </summary>
    public const string WindowsLocalAppDataPath = @"C:\users\steamuser\AppData\Local";

    /// <summary>
    /// Walks up from any path inside a Steam library's <c>steamapps/common/...</c> tree (an
    /// install root as <see cref="AbioticEditor.Core.Assets.AfInstallLocator"/> or
    /// <see cref="AbioticEditor.Core.Assets.GameInstallLocator"/> return it, in any of the shapes
    /// each accepts) to that library's own root folder - the same folder
    /// <c>AbioticEditor.Core.Saves.SaveDiscovery.DiscoverProtonClientWorlds</c> is handed for its
    /// own <c>steamapps/compatdata</c> scan. Returns null when <paramref name="installPath"/> is
    /// not inside a <c>steamapps/common</c> tree at all (a Custom install the player pointed at
    /// directly, outside any Steam library - Proton mapping does not apply there).
    /// </summary>
    public static string? FindSteamLibraryRoot(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(installPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        var parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("steamapps", StringComparison.OrdinalIgnoreCase)
                && parts[i + 1].Equals("common", StringComparison.OrdinalIgnoreCase))
            {
                return i == 0 ? Path.DirectorySeparatorChar.ToString() : string.Join(Path.DirectorySeparatorChar, parts[..i]);
            }
        }
        return null;
    }

    /// <summary>
    /// The Wine prefix root (<c>.../steamapps/compatdata/&lt;appId&gt;/pfx</c>) for
    /// <paramref name="steamLibraryRoot"/>, or null when that prefix has never been created (the
    /// game has never actually been launched through Steam Play yet on this library) or does not
    /// exist for some other reason. Never throws.
    /// </summary>
    public static string? FindPrefixRoot(string steamLibraryRoot, int appId)
    {
        if (string.IsNullOrWhiteSpace(steamLibraryRoot)) return null;
        var prefix = Path.Combine(
            steamLibraryRoot, "steamapps", "compatdata",
            appId.ToString(System.Globalization.CultureInfo.InvariantCulture), "pfx");
        try
        {
            return Directory.Exists(prefix) ? prefix : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The folder a Windows <c>%LOCALAPPDATA%</c> resolves to, physically, for
    /// <paramref name="prefixRoot"/>'s default <see cref="PrefixUser"/> account - i.e. where a
    /// process running inside that prefix with <see cref="WindowsLocalAppDataPath"/> as its
    /// <c>LOCALAPPDATA</c> actually reads and writes files on the real Linux filesystem.</summary>
    public static string LocalAppDataIn(string prefixRoot)
        => Path.Combine(prefixRoot, "drive_c", "users", PrefixUser, "AppData", "Local");
}
