using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.LiveEditing;

namespace AbioticEditor.Web.Services;

/// <summary>Where <see cref="LiveAgentSetup.EnsureReadyAsync"/> got to.</summary>
public enum LiveAgentSetupState
{
    /// <summary>The in-game side is deployed and its helper process is (or was just) running.
    /// A local connection attempt can proceed exactly as before.</summary>
    Ready,

    /// <summary>Legacy setup state retained for compatibility.</summary>
    NeedsUe4ss,

    /// <summary>No local Abiotic Factor install could be found at all (see
    /// <see cref="AfInstallLocator.FindPaksDirectory"/>).</summary>
    GameNotFound,

    /// <summary>The release is missing its bundled helper or agent files.</summary>
    HelperUnavailable,

    /// <summary>Installing the runtime or updating the bundled agent needs consent.
    /// Detail names the game folder. Nothing has been written yet.</summary>
    NeedsConsentToDeploy,

    /// <summary>This host's operating system cannot run either half of live editing's in-game
    /// side (the bundled helper is a Windows binary, and UE4SS itself is Windows-only) - not to
    /// be confused with <see cref="GameNotFound"/>, which means the same OS just couldn't locate
    /// an install. A dedicated server the player connects to remotely is unaffected; only the
    /// automatic "this PC" setup is unavailable here.</summary>
    NotSupportedOnThisPlatform,

    /// <summary>Setup failed; Detail contains the recovery instructions.</summary>
    SetupFailed,
}

public sealed record LiveAgentSetupResult(LiveAgentSetupState State, string? Detail = null);

/// <summary>Prepares local live editing on Windows: downloads a missing runtime after consent,
/// updates the bundled agent, and starts the helper. Existing mod installations are preserved.</summary>
public static class LiveAgentSetup
{
    private static readonly HttpClient SetupHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
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

    /// <param name="deployConsentGiven">
    /// True once the player has agreed to let this write the bundled script into their game's
    /// Mods folder. Pass false the first time; if the result is
    /// <see cref="LiveAgentSetupState.NeedsConsentToDeploy"/>, nothing was written - ask, then
    /// call again with true to actually deploy. Irrelevant (never even checked) when the mod is
    /// already there and up to date, so a normal reconnect never re-prompts.
    /// </param>
    public static async Task<LiveAgentSetupResult> EnsureReadyAsync(bool deployConsentGiven, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(LiveAgentSetupState.NotSupportedOnThisPlatform);
        }

        var paksDirectory = AfInstallLocator.FindPaksDirectory();
        if (paksDirectory is null)
        {
            return new(LiveAgentSetupState.GameNotFound);
        }

        // <root>/Content/Paks -> <root>/Content -> <root>, the project folder that Binaries sits
        // beside - true regardless of which of the two shapes ResolvePaksDirectory matched.
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(paksDirectory));
        if (string.IsNullOrEmpty(projectRoot))
        {
            return new(LiveAgentSetupState.GameNotFound);
        }

        var win64 = Path.Combine(projectRoot, "Binaries", "Win64");
        var modsDir = Path.Combine(win64, "ue4ss", "Mods");
        if (!Directory.Exists(BundledScriptsDir) || (!File.Exists(BundledHelperPath) && !IsHelperRunning()))
            return new(LiveAgentSetupState.HelperUnavailable,
                "This copy of the editor is missing live-support files. Extract the full Windows release and try again.");

        if (!LiveRuntimeInstaller.IsInstalled(win64) || !IsModUpToDate(modsDir))
        {
            if (!deployConsentGiven)
                return new(LiveAgentSetupState.NeedsConsentToDeploy, win64);
            if (IsGameRunning())
                return new(LiveAgentSetupState.SetupFailed, "Close Abiotic Factor or stop its server, then retry setup.");
            try
            {
                await new LiveRuntimeInstaller(SetupHttp).InstallAsync(win64, cancellationToken).ConfigureAwait(false);
                DeployMod(modsDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return new(LiveAgentSetupState.SetupFailed,
                    "Setup could not finish. Check your internet connection and game-folder permissions, then retry. " + ex.Message);
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

        try
        {
            LaunchHelperHidden();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            return new(LiveAgentSetupState.HelperUnavailable, ex.Message);
        }

        // Give it a moment to open its listener and write its token before the caller starts
        // polling for one - the first poll landing before either exists just costs a needless
        // 2-second retry, but there is no signal to await instead, and the process just started.
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        return new(LiveAgentSetupState.Ready);
    }

    // Guarded by the OperatingSystem.IsWindows() check at the top of EnsureReadyAsync (the only
    // caller) - annotated so the platform-compat analyzer can verify that instead of flagging
    // Process.GetProcessesByName as reachable on every platform this assembly also ships on
    // (the browser/Wasm host, which never registers ILiveEditingCapability and so never calls in
    // here at all, but still compiles this file).
    [SupportedOSPlatform("windows")]
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

    [SupportedOSPlatform("windows")]
    private static bool IsHelperRunning()
    {
        foreach (var process in Process.GetProcessesByName(HelperProcessName))
        {
            process.Dispose();
            return true;
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
    [SupportedOSPlatform("windows")]
    private static void LaunchHelperHidden()
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

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(BundledHelperPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(BundledHelperPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
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
