using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.Steam;

namespace AbioticEditor.Web.Services;

/// <summary>Where <see cref="LiveAgentSetup.EnsureReadyAsync"/> got to.</summary>
public enum LiveAgentSetupState
{
    /// <summary>The in-game side is deployed and its helper process is (or was just) running.
    /// A local connection attempt can proceed exactly as before.</summary>
    Ready,

    /// <summary>UE4SS is missing or incomplete. Detail is the folder the game's executable runs
    /// from (<c>Binaries\Win64</c> on Steam, <c>Binaries\WinGDK</c> on Game Pass) - the one
    /// UE4SS has to be installed into.</summary>
    NeedsUe4ss,

    /// <summary>No local Abiotic Factor install could be found at all (see
    /// <see cref="AfInstallLocator.FindPaksDirectory"/>).</summary>
    GameNotFound,

    /// <summary>The release is missing its bundled helper or agent files.</summary>
    HelperUnavailable,

    /// <summary>Installing or updating the bundled agent needs consent.
    /// Detail names the game folder. Nothing has been written yet.</summary>
    NeedsConsentToDeploy,

    /// <summary>UE4SS is missing but this release bundles it; Detail is the executable folder it
    /// would be installed into (see <see cref="NeedsUe4ss"/>). Nothing has been written.</summary>
    NeedsConsentToInstallUe4ss,

    /// <summary>This host's operating system cannot run either half of live editing's in-game
    /// side: the bundled helper is a Windows binary, and UE4SS itself is Windows-only. Windows
    /// and Linux (the game running under Steam Play/Proton, still the same Windows binaries -
    /// see <see cref="ProtonLiveAgentEnvironment"/>) both support the automatic "this PC" setup;
    /// this state is for everything else (macOS and any other platform this editor ships on).
    /// Not to be confused with <see cref="GameNotFound"/>, which means the same OS just couldn't
    /// locate an install. A dedicated server the player connects to remotely is unaffected; only
    /// the automatic "this PC" setup is unavailable here.</summary>
    NotSupportedOnThisPlatform,

    /// <summary>Setup failed; Detail contains the recovery instructions.</summary>
    SetupFailed,
}

public sealed record LiveAgentSetupResult(LiveAgentSetupState State, string? Detail = null);

/// <summary>Checks for UE4SS (installing the bundled copy after consent when this release ships
/// one), updates the bundled agent after consent, and starts the helper. A build without a
/// bundled UE4SS package falls back to asking the player to install it separately.</summary>
public static class LiveAgentSetup
{
    private const string ModFolderName = "AbioticEditorLiveAgentLua";
    private const string HelperProcessName = "AbioticEditorLiveAgentHelper";

    /// <summary>
    /// The port the helper tries first. It falls back to a nearby one on its own if this is
    /// already taken (another helper instance, or anything else on the machine - see
    /// LiveAgentServer::Start in the native helper) and writes whichever one it actually bound to
    /// into <c>port.txt</c> next to its token, so <see cref="ILiveEditingCapability.TryReadLocalPort"/>
    /// is what a local connection attempt should always use; this constant is only the fallback
    /// for the brief moment before that file exists, or an old helper build that never wrote one.
    /// </summary>
    public const int DefaultPort = 42117;

    /// <summary>
    /// The bundled Lua script tree this build ships (see the csproj's <c>live-agent\Lua\Scripts</c>
    /// content), or null when this build has none (a dev build of the editor itself that never
    /// bundled one - see <see cref="LiveAgentSetupState.HelperUnavailable"/> for the equivalent
    /// case on the helper side).
    /// </summary>
    private static string BundledScriptsDir => Path.Combine(AppContext.BaseDirectory, "live-agent", "Lua", "Scripts");
    private static string BundledHelperPath => Path.Combine(AppContext.BaseDirectory, "live-agent", $"{HelperProcessName}.exe");
    private static string BundledUe4ssDir => Path.Combine(AppContext.BaseDirectory, "live-agent", "ue4ss");

    private static readonly Lazy<Ue4ssRuntimeManifest?> LazyBundledUe4ss = new(() => Ue4ssBundledRuntime.TryLoad(BundledUe4ssDir)?.Manifest);

    /// <summary>The pinned UE4SS package this build bundles, or null when this build (a dev build,
    /// or a non-Windows release) has none. See <see cref="Ue4ssBundledRuntime"/>.</summary>
    public static Ue4ssRuntimeManifest? BundledUe4ss => LazyBundledUe4ss.Value;

    /// <param name="deployConsentGiven">
    /// True once the player has agreed to let this write the bundled script into their game's
    /// Mods folder. Pass false the first time; if the result is
    /// <see cref="LiveAgentSetupState.NeedsConsentToDeploy"/>, nothing was written - ask, then
    /// call again with true to actually deploy. Irrelevant (never even checked) when the mod is
    /// already there and up to date, so a normal reconnect never re-prompts.
    /// </param>
    /// <param name="installUe4ssConsentGiven">
    /// True once the player has agreed to let this install the bundled UE4SS package. Pass false
    /// the first time; if the result is <see cref="LiveAgentSetupState.NeedsConsentToInstallUe4ss"/>,
    /// nothing was written - ask, then call again with true to actually install. Only relevant
    /// when UE4SS is missing and this build bundles it (see <see cref="BundledUe4ss"/>); when
    /// UE4SS is already present, or this build has no bundle, it is never even checked. Consenting
    /// to install UE4SS also counts as consent to deploy this editor's own agent - the one dialog
    /// covers both.
    /// </param>
    /// <param name="gameFolder">
    /// The game install to set up, as the player chose it on the "This PC" step (any folder
    /// shape <see cref="AfInstallLocator.ResolvePaksDirectory"/> accepts). Null means the
    /// configured install (<see cref="GameInstallLocator.FindConfigured"/>): the folder saved
    /// in Settings, else the first copy detected on this machine. A Game Pass copy is set up in
    /// its <c>Binaries\WinGDK</c> folder, a Steam copy in <c>Binaries\Win64</c> - see
    /// <see cref="GameInstall.BinariesDirectory"/>.
    /// </param>
    public static async Task<LiveAgentSetupResult> EnsureReadyAsync(
        bool deployConsentGiven, bool installUe4ssConsentGiven = false, string? gameFolder = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            // macOS (and anything else): neither the bundled helper nor UE4SS itself has a build
            // for this platform, and there is no Proton-equivalent way to run the Windows ones
            // here either (there is a launch-mac.sh for the editor itself, but that is unrelated
            // to the in-game side). Windows and Linux (Steam Play/Proton) both fall through past
            // this check - see ProtonLiveAgentEnvironment for how Linux resolves the same
            // Windows-shaped paths Proton gives the game itself.
            return new(LiveAgentSetupState.NotSupportedOnThisPlatform);
        }

        var install = gameFolder is null ? GameInstallLocator.FindConfigured() : GameInstallLocator.Resolve(gameFolder);
        if (install is null)
        {
            return new(LiveAgentSetupState.GameNotFound);
        }

        var binaries = install.BinariesDirectory;
        var modsDir = Ue4ssInstallation.FindModsDirectory(binaries);
        if (modsDir is null)
        {
            var bundle = Ue4ssBundledRuntime.TryLoad(BundledUe4ssDir);
            if (bundle is null) return new(LiveAgentSetupState.NeedsUe4ss, binaries);
            if (!installUe4ssConsentGiven) return new(LiveAgentSetupState.NeedsConsentToInstallUe4ss, binaries);
            if (IsGameRunning())
                return new(LiveAgentSetupState.SetupFailed, "Close Abiotic Factor or stop its server, then retry setup.");
            if (LockedFolderMessage(install) is { } locked) return new(LiveAgentSetupState.SetupFailed, locked);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await bundle.InstallAsync(binaries, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new(LiveAgentSetupState.SetupFailed, ex.Message);
            }

            // One consent dialog covers both installing UE4SS and deploying this editor's own
            // agent into the Mods folder that install just created.
            deployConsentGiven = true;
            modsDir = Ue4ssInstallation.FindModsDirectory(binaries);
            if (modsDir is null) return new(LiveAgentSetupState.SetupFailed, "UE4SS setup did not finish. Retry setup.");
        }

        if (!Directory.Exists(BundledScriptsDir) || (!File.Exists(BundledHelperPath) && !IsHelperRunning()))
            return new(LiveAgentSetupState.HelperUnavailable,
                "This copy of the editor is missing live-support files. Extract the full Windows release and try again.");

        if (!IsModUpToDate(modsDir))
        {
            if (!deployConsentGiven)
                return new(LiveAgentSetupState.NeedsConsentToDeploy, Path.Combine(modsDir, ModFolderName));
            if (IsGameRunning())
                return new(LiveAgentSetupState.SetupFailed, "Close Abiotic Factor or stop its server, then retry setup.");
            if (LockedFolderMessage(install) is { } locked) return new(LiveAgentSetupState.SetupFailed, locked);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeployMod(modsDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new(LiveAgentSetupState.SetupFailed,
                    "Setup could not finish. Check game-folder permissions, then retry. " + ex.Message);
            }
        }

        if (IsHelperRunning())
        {
            return new(LiveAgentSetupState.Ready);
        }

        if (!File.Exists(BundledHelperPath))
        {
            return new(LiveAgentSetupState.HelperUnavailable);
        }

        // The helper is still a Windows binary on Linux too - it only ever runs inside the same
        // Steam Play (Proton) prefix the game itself runs in, with a matching LOCALAPPDATA, so
        // its token/port files and the request/response file mailbox land where the in-game Lua
        // mod (running inside that same prefix) can see them. See ProtonLiveAgentEnvironment.
        string? linuxPrefixRoot = null;
        if (OperatingSystem.IsLinux())
        {
            var libraryRoot = ProtonLiveAgentEnvironment.FindSteamLibraryRoot(install.Root);
            linuxPrefixRoot = libraryRoot is null
                ? null
                : ProtonLiveAgentEnvironment.FindPrefixRoot(libraryRoot, SteamAchievements.AppId);
            if (linuxPrefixRoot is null)
            {
                return new(LiveAgentSetupState.HelperUnavailable,
                    "Could not find this game's Steam Play (Proton) profile yet. Launch Abiotic Factor through "
                    + "Steam at least once, close it, then retry live-editing setup.");
            }
            if (!IsWineAvailable())
            {
                return new(LiveAgentSetupState.HelperUnavailable,
                    "Live editing on Linux runs a small helper program through Wine alongside the game (the game "
                    + "itself already has everything it needs - UE4SS and this editor's mod are installed the same "
                    + "way as on Windows). Install your distro's 'wine' package (or point the ABIOTIC_LIVE_WINE "
                    + "environment variable at a wine/Proton binary), then retry setup.");
            }
        }

        try
        {
            EditorLog.Info("LiveAgent", "No helper process found running - launching one.");
            LaunchHelperHidden(linuxPrefixRoot);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            EditorLog.Warn("LiveAgent", "Could not launch the live-agent helper.", ex);
            return new(LiveAgentSetupState.HelperUnavailable, ex.Message);
        }

        // Give it a moment to open its listener and write its token before the caller starts
        // polling for one - the first poll landing before either exists just costs a needless
        // 2-second retry, but there is no signal to await instead, and the process just started.
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        return new(LiveAgentSetupState.Ready);
    }

    /// <summary>
    /// A plain-words explanation when nothing can be written into the game's executable folder,
    /// or null when it is writable. Checked before every write rather than after it fails: the
    /// Xbox app installs a Game Pass game into a folder it keeps locked until the player turns on
    /// mods for that game, and the bare "access denied" a write throws there tells a player
    /// nothing about that switch.
    /// </summary>
    private static string? LockedFolderMessage(GameInstall install)
    {
        if (GameInstallLocator.CanWriteTo(install.BinariesDirectory)) return null;
        if (!Directory.Exists(install.BinariesDirectory))
            return $"The folder the game runs from was not found at {install.BinariesDirectory}. Choose the game folder again, or repair the game in its store app.";
        return install.Kind == GameInstallKind.GamePass
            ? "The Xbox app keeps this Game Pass copy's folder locked until mods are turned on for the game. In the Xbox app, open Abiotic Factor, use the ... menu and choose Enable mods, then retry."
            : $"Nothing can be written into {install.BinariesDirectory}. Check that folder's permissions, then retry.";
    }

    // Guarded by the OperatingSystem.IsWindows()/IsLinux() check at the top of EnsureReadyAsync
    // (the only caller) - annotated so the platform-compat analyzer can verify that instead of
    // flagging Process.GetProcessesByName as reachable on every platform this assembly also ships
    // on (the browser/Wasm host, which never registers ILiveEditingCapability and so never calls
    // in here at all, but still compiles this file).
    //
    // NOTE (Linux/Proton, unverified against a real install): on Windows this matches the
    // shipping executable's own process name. Under Proton the game runs inside Wine, and
    // whether the resulting Linux process is actually named "AbioticFactor..." (rather than a
    // "wine"/"wine64-preloader" wrapper, or the Windows name truncated by the kernel's 15-byte
    // comm-name limit) has not been confirmed on a real Steam Play session - see
    // docs/PROGRESS.md's Linux live-editing round for this open item.
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    private static bool IsGameRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.ProcessName.StartsWith("AbioticFactor", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    // Same Linux caveat as IsGameRunning above: a helper launched through Wine may not show up
    // under Linux as a process literally named "AbioticEditorLiveAgentHelper" (Wine's own process
    // naming for the child Windows executable is not confirmed here) - unverified.
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    private static bool IsHelperRunning()
    {
        foreach (var process in Process.GetProcessesByName(HelperProcessName))
        {
            process.Dispose();
            return true;
        }
        return false;
    }

    private const string WineOverrideEnvVar = "ABIOTIC_LIVE_WINE";

    /// <summary>Whether a Wine binary this editor can hand the native helper to actually exists:
    /// either <see cref="WineOverrideEnvVar"/> pointing straight at one, or a <c>wine</c> found on
    /// PATH. Filesystem-only (never launches anything), so a missing Wine install can be reported
    /// with a clear message instead of a raw process-start failure.</summary>
    [SupportedOSPlatform("linux")]
    private static bool IsWineAvailable()
    {
        var wineOverride = Environment.GetEnvironmentVariable(WineOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(wineOverride))
        {
            return File.Exists(wineOverride);
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable)) return false;
        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;
            try
            {
                if (File.Exists(Path.Combine(directory, "wine"))) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Probe the next PATH entry.
            }
        }
        return false;
    }

    /// <summary>
    /// Kept alive for the app's lifetime so its output-redirection handles are not closed out
    /// from under it by the garbage collector - see <see cref="LaunchHelperHidden"/>.
    /// </summary>
    private static Process? _helperProcess;

    /// <summary>
    /// Launches the bundled helper with no console window - a player who never asked to see a
    /// "keep this window open" console should not have one appear - and pipes its stdout/stderr
    /// into a single rolling log file instead, so a crash or a printed error still leaves
    /// something to look at when live editing does not connect.
    /// </summary>
    /// <param name="linuxPrefixRoot">On Linux, the Steam Play (Proton) prefix resolved by the
    /// caller (see <see cref="ProtonLiveAgentEnvironment"/>) - required there, since the helper
    /// (still a Windows binary) has to run through Wine inside that exact prefix to see the same
    /// <c>%LOCALAPPDATA%</c> the in-game Lua mod does. Ignored on Windows.</param>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    private static void LaunchHelperHidden(string? linuxPrefixRoot)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticEditorLiveAgent", "helper.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        // Overwritten each launch: this process only ever matters for the live-editing session
        // that just started it, and an ever-growing log nobody rotates helps nobody.
        var writer = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var sync = new object();
        void WriteLine(string? line)
        {
            if (line is null) return;
            lock (sync) writer.WriteLine(line);
        }

        var startInfo = OperatingSystem.IsLinux()
            ? BuildLinuxWineStartInfo(linuxPrefixRoot
                ?? throw new InvalidOperationException("Live editing on Linux needs a resolved Proton prefix before launching the helper."))
            : new ProcessStartInfo(BundledHelperPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(BundledHelperPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) => WriteLine(e.Data);
        process.ErrorDataReceived += (_, e) => WriteLine(e.Data);
        process.Exited += (_, _) => { lock (sync) { writer.Flush(); writer.Dispose(); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _helperProcess = process;
    }

    /// <summary>
    /// Runs the (still-Windows) helper executable through Wine, pinned to the same Proton prefix
    /// the game itself runs in and given a matching <c>LOCALAPPDATA</c>, so the token/port files
    /// and the request/response file mailbox it writes land exactly where the in-game Lua mod
    /// (running inside that same prefix, launched by Steam) looks for them - see
    /// <see cref="ProtonLiveAgentEnvironment"/>. Not verified against a real Steam Play session;
    /// see <see cref="IsWineAvailable"/>'s caller for the player-facing fallback message when no
    /// Wine binary can be found at all.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static ProcessStartInfo BuildLinuxWineStartInfo(string prefixRoot)
    {
        var wineOverride = Environment.GetEnvironmentVariable(WineOverrideEnvVar);
        var wineExecutable = string.IsNullOrWhiteSpace(wineOverride) ? "wine" : wineOverride;
        var startInfo = new ProcessStartInfo(wineExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(BundledHelperPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(BundledHelperPath);
        startInfo.Environment["WINEPREFIX"] = prefixRoot;
        startInfo.Environment["LOCALAPPDATA"] = ProtonLiveAgentEnvironment.WindowsLocalAppDataPath;
        return startInfo;
    }

    /// <summary>Checks every bundled script, including area modules updated independently of main.lua.</summary>
    private static bool IsModUpToDate(string modsDir)
    {
        if (!Directory.Exists(BundledScriptsDir)) return true;

        try
        {
            return Directory.EnumerateFiles(BundledScriptsDir, "*", SearchOption.AllDirectories).All(file =>
            {
                var target = Path.Combine(modsDir, ModFolderName, "Scripts", Path.GetRelativePath(BundledScriptsDir, file));
                return File.Exists(target) && File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(target));
            }) && IsEnabledInModsList(modsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Can't tell - safer to say "needs a look" (which asks before doing anything) than
            // to silently skip a deploy that turns out to matter.
            return false;
        }
    }

    private static bool IsEnabledInModsList(string modsDir)
    {
        var modsTxtPath = Path.Combine(modsDir, "mods.txt");
        if (!File.Exists(modsTxtPath)) return false;
        return File.ReadAllLines(modsTxtPath).Any(line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith(ModFolderName, StringComparison.OrdinalIgnoreCase)
                && trimmed.EndsWith('1');
        });
    }

    /// <summary>
    /// Actually writes the bundled script into <paramref name="modsDir"/> and enables it - only
    /// ever called after <see cref="EnsureReadyAsync"/> has either confirmed it is unnecessary
    /// (<see cref="IsModUpToDate"/>) or the player has explicitly consented
    /// (<see cref="LiveAgentSetupState.NeedsConsentToDeploy"/>).
    /// </summary>
    private static void DeployMod(string modsDir)
    {
        if (!Directory.Exists(BundledScriptsDir))
        {
            // Nothing bundled to deploy - a dev build of the editor itself, not the game's UE4SS
            // Mods folder (already confirmed present above). Leave whatever is already installed
            // there untouched rather than deleting it.
            return;
        }

        CopyDirectory(BundledScriptsDir, Path.Combine(modsDir, ModFolderName, "Scripts"));
        EnableInModsList(modsDir);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Adds or updates exactly the one <c>&lt;ModFolderName&gt; : 1</c> line this mod owns.
    /// Every other line - every other mod's entry, comments, ordering - is left byte-for-byte
    /// alone, the same "never rewrite what you did not come here to change" rule the save
    /// writers follow.
    /// </summary>
    private static void EnableInModsList(string modsDir)
    {
        var modsTxtPath = Path.Combine(modsDir, "mods.txt");
        var lines = File.Exists(modsTxtPath) ? File.ReadAllLines(modsTxtPath).ToList() : [];
        var index = lines.FindIndex(line =>
            line.TrimStart().StartsWith(ModFolderName + " ", StringComparison.OrdinalIgnoreCase)
            || line.TrimStart().StartsWith(ModFolderName + ":", StringComparison.OrdinalIgnoreCase));

        const string enabledLine = ModFolderName + " : 1";
        if (index < 0)
        {
            lines.Add(enabledLine);
        }
        else if (!string.Equals(lines[index].Trim(), enabledLine, StringComparison.Ordinal))
        {
            lines[index] = enabledLine;
        }
        else
        {
            return;
        }
        File.WriteAllLines(modsTxtPath, lines);
    }
}
