using System.IO;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Marker for whether this host offers live in-game editing. Registered only by the desktop
/// host's <c>Program.cs</c> - the WASM host never registers it, so
/// <c>MainLayout.razor</c> resolves it through <see cref="IServiceProvider.GetService"/> rather
/// than a required <c>[Inject]</c>, and skips both the automatic "what do you want to do"
/// prompt and its header button when it is absent instead of throwing. This is the entire
/// mechanism that keeps live editing out of the browser build with no `#if`/conditional-compile
/// split anywhere in the shared screens.
/// </summary>
public interface ILiveEditingCapability
{
    bool IsAvailable { get; }

    /// <summary>
    /// Reads the live-agent's connection token straight off this PC, if the mod has ever written
    /// one, so <c>LiveConnect.razor</c> can connect to a locally hosted game without asking the
    /// player to copy/paste anything - only a dedicated server (a different machine) still needs
    /// the manual host/port/token form. Returns null on the WASM host (no local filesystem to
    /// read) and whenever no local live-agent has run yet.
    /// </summary>
    string? TryReadLocalToken();

    /// <summary>
    /// Reads the port the local live-agent helper actually bound to, if it has ever run and
    /// written one. The helper's fixed preferred port can be taken (another helper instance
    /// left running, or anything else on the machine), in which case it falls back to a nearby
    /// port on its own and records the real one here - see <c>LiveAgentServer::Start</c> in the
    /// native helper. Falls back to <see cref="LiveAgentSetup.DefaultPort"/> when no such file
    /// exists yet (an old helper build, or the brief moment right after launch before it has
    /// written one), matching this helper's own preferred port.
    /// </summary>
    int TryReadLocalPort();
}

/// <summary>The desktop host's registration: live editing is always offered there.</summary>
public sealed class DesktopLiveEditingCapability : ILiveEditingCapability
{
    public bool IsAvailable => true;

    public string? TryReadLocalToken() => TryReadFile("token.txt");

    public int TryReadLocalPort()
    {
        var text = TryReadFile("port.txt");
        return int.TryParse(text, out var port) ? port : LiveAgentSetup.DefaultPort;
    }

    private static string? TryReadFile(string fileName)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AbioticEditorLiveAgent", fileName);
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
