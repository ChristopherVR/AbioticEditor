using AbioticEditor.Core.Plugins;

namespace AbioticEditor.Core.LiveEditing;

/// <summary>
/// Where live editing keeps its working files: the helper's <c>token.txt</c>/<c>port.txt</c>, the
/// request/response mailbox (<c>ipc\</c>) and the two diagnostic logs. Three separate processes
/// have to agree on it - this editor, the native helper it launches, and the UE4SS Lua mod
/// running inside the game - and by default each derives
/// <c>%LOCALAPPDATA%\AbioticEditorLiveAgent</c> on its own.
///
/// <para>With <c>ABIOTIC_APPDATA_DIR</c> set (a portable install, see
/// <see cref="PluginPaths.AppDataRoot"/>) the folder moves to <c>live-agent</c> under that root
/// instead. The helper is told through <see cref="HelperDirectoryEnvVar"/> when the editor
/// launches it; the game is started by Steam and never sees this editor's environment, so the
/// Lua mod is told through a small generated script deployed next to it (see
/// <see cref="LuaPointerFileName"/>).</para>
/// </summary>
public static class LiveAgentPaths
{
    /// <summary>The folder name under <c>%LOCALAPPDATA%</c> all three sides default to.</summary>
    public const string DefaultFolderName = "AbioticEditorLiveAgent";

    /// <summary>The folder name under a redirected <see cref="PluginPaths.AppDataRoot"/>.</summary>
    public const string RedirectedFolderName = "live-agent";

    /// <summary>Environment variable the native helper reads its working folder from (see
    /// <c>LiveAgentDir</c> in the helper's TokenStore.h). Unset means the default folder.</summary>
    public const string HelperDirectoryEnvVar = "ABIOTIC_LIVE_AGENT_DIR";

    /// <summary>The generated Lua module, deployed into the mod's <c>Scripts</c> folder, that
    /// main.lua loads with <c>require("datadir")</c> to learn a redirected folder. Absent means
    /// the default folder.</summary>
    public const string LuaPointerFileName = "datadir.lua";

    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultFolderName);

    private static readonly string? RedirectedRoot = ResolveRedirectedRoot(
        Environment.GetEnvironmentVariable("ABIOTIC_APPDATA_DIR"));

    /// <summary>Where this editor writes the files only it produces (<c>helper.log</c>). Always a
    /// native path of this process, redirected on every OS.</summary>
    public static string HostRoot { get; } = RedirectedRoot ?? DefaultRoot;

    /// <summary>
    /// The redirected folder the helper and the Lua mod must both be pointed at, or null when
    /// they keep their own default. Windows only: on Linux both run inside the game's Steam Play
    /// (Proton) prefix, which already keeps their files out of this user's own profile (see
    /// <see cref="ProtonLiveAgentEnvironment"/>). Also null for a path the two can not be handed
    /// safely (see <see cref="IsSafeForNativeSides"/>).
    /// </summary>
    public static string? RedirectedSharedRoot { get; } =
        OperatingSystem.IsWindows() && RedirectedRoot is { } root && IsSafeForNativeSides(root) ? root : null;

    /// <summary>The folder holding the token, port, mailbox and <c>lua.log</c> as this process
    /// sees it natively (on Linux the Proton prefix is probed first, by the callers).</summary>
    public static string SharedRoot { get; } = RedirectedSharedRoot ?? DefaultRoot;

    /// <summary>The <c>live-agent</c> folder under an <c>ABIOTIC_APPDATA_DIR</c> value, as an
    /// absolute path (the helper and the game each have a different working directory, so a
    /// relative one would mean three different folders), or null when the variable is unset.</summary>
    public static string? ResolveRedirectedRoot(string? appDataOverride)
    {
        if (string.IsNullOrWhiteSpace(appDataOverride)) return null;
        try
        {
            return Path.Combine(Path.GetFullPath(appDataOverride.Trim()), RedirectedFolderName);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> survives the trip to the helper and the Lua mod unchanged.
    /// Both open files through the C runtime's narrow (ANSI code page) calls, so a path with
    /// characters outside printable ASCII could resolve to a different folder on each side, or
    /// none; and it has to fit inside the Lua long string <see cref="LuaPointerContent"/> writes.
    /// </summary>
    public static bool IsSafeForNativeSides(string path)
        => path.Length is > 0 and < 200
            && path.All(c => c is >= ' ' and <= '~')
            && !path.Contains("]==]", StringComparison.Ordinal);

    /// <summary>The exact text of <see cref="LuaPointerFileName"/> for <paramref name="root"/>.</summary>
    public static string LuaPointerContent(string root)
        => "-- Written by Abiotic Editor: where its live-editing files live (a portable data folder).\n"
            + "-- Safe to delete; the editor then uses the default folder again on its next setup.\n"
            + "return [==[" + root + "]==]\n";
}
