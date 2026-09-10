using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Web.Services;

/// <summary>Where <see cref="LiveAgentSetup.EnsureReadyAsync"/> got to.</summary>
public enum LiveAgentSetupState
{
    /// <summary>The in-game side is deployed and its helper process is (or was just) running.
    /// A local connection attempt can proceed exactly as before.</summary>
    Ready,

    /// <summary>A game install was found, but it has no <c>ue4ss</c> folder - the one
    /// prerequisite this cannot install on the player's behalf (a separate, third-party
    /// framework). Live editing cannot work here until that's installed once, by hand.</summary>
    NeedsUe4ss,

    /// <summary>No local Abiotic Factor install could be found at all (see
    /// <see cref="AfInstallLocator.FindPaksDirectory"/>).</summary>
    GameNotFound,

    /// <summary>UE4SS is present and the script was deployed, but this build of the editor has
    /// no bundled helper process to launch (a local dev build that never ran the native build -
    /// see live-agent/README.md). Falls back to hoping one is already running, exactly like
    /// the fully manual setup this replaces.</summary>
    HelperUnavailable,

    /// <summary>UE4SS is present, but the bundled script is missing or out of date in its Mods
    /// folder and nothing has been written there yet - call <see cref="EnsureReadyAsync"/> again
    /// with <c>deployConsentGiven: true</c> to actually copy it, only once the player has agreed
    /// (<see cref="LiveAgentSetupResult.Detail"/> names the exact folder that would be written
    /// to). Nothing is touched on disk while this state is returned.</summary>
    NeedsConsentToDeploy,

    /// <summary>This host's operating system cannot run either half of live editing's in-game
    /// side (the bundled helper is a Windows binary, and UE4SS itself is Windows-only) - not to
    /// be confused with <see cref="GameNotFound"/>, which means the same OS just couldn't locate
    /// an install. A dedicated server the player connects to remotely is unaffected; only the
    /// automatic "this PC" setup is unavailable here.</summary>
    NotSupportedOnThisPlatform,
}

public sealed record LiveAgentSetupResult(LiveAgentSetupState State, string? Detail = null);

/// <summary>
/// Prepares a detected local Abiotic Factor install for live editing with nothing for the
/// player to do by hand: deploys the bundled Lua script into UE4SS's own <c>Mods</c> folder
/// (enabling it in <c>mods.txt</c>, touching nothing else there) and launches the bundled helper
/// process if one is not already running. Windows-only - UE4SS, and Abiotic Factor's own live
/// editing support, are both Windows-only today.
///
/// <para>Deliberately narrow: this never installs UE4SS itself (a separate, third-party
/// framework - see <see cref="LiveAgentSetupState.NeedsUe4ss"/>), never touches any other mod's
/// files, and never rewrites <c>mods.txt</c> beyond the one line that enables this mod.</para>
/// </summary>
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

        var modsDir = Path.Combine(projectRoot, "Binaries", "Win64", "ue4ss", "Mods");
        if (!Directory.Exists(modsDir))
        {
            return new(LiveAgentSetupState.NeedsUe4ss);
        }

        if (!IsModUpToDate(modsDir))
        {
            if (!deployConsentGiven)
            {
                return new(LiveAgentSetupState.NeedsConsentToDeploy, Path.Combine(modsDir, ModFolderName));
            }
            try
            {
                DeployMod(modsDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A Mods folder that exists but refuses writes reads the same as UE4SS not being
                // properly set up - the fix (permissions, an antivirus lock, ...) is the same kind
                // of one-time thing the set-up guide already walks through.
                return new(LiveAgentSetupState.NeedsUe4ss, ex.Message);
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

    /// <summary>
    /// True when there is nothing this would need to copy or change: either this build has no
    /// bundled script to compare against (a dev build - see <see cref="BundledScriptsDir"/>), or
    /// the destination already carries an identical copy and is already enabled. Comparing just
    /// <c>main.lua</c> byte-for-byte is a deliberately cheap stand-in for the whole tree: every
    /// file in it ships and updates together as one unit, so an identical entry point means an
    /// identical everything-else too.
    /// </summary>
    private static bool IsModUpToDate(string modsDir)
    {
        if (!Directory.Exists(BundledScriptsDir)) return true;

        try
        {
            var bundledMain = Path.Combine(BundledScriptsDir, "main.lua");
            var deployedMain = Path.Combine(modsDir, ModFolderName, "Scripts", "main.lua");
            if (!File.Exists(bundledMain) || !File.Exists(deployedMain)) return false;
            return File.ReadAllBytes(bundledMain).AsSpan().SequenceEqual(File.ReadAllBytes(deployedMain))
                && IsEnabledInModsList(modsDir);
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
