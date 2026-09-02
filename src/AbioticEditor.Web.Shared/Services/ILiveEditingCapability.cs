using System.IO;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Marker for whether this host offers live in-game editing. Registered only by the desktop
/// host's <c>Program.cs</c> - the WASM host never registers it, so
/// <c>ModeSelect.razor</c> resolves it through <see cref="IServiceProvider.GetService"/> rather
/// than a required <c>[Inject]</c>, and skips straight to the file-editing flow when it is
/// absent instead of throwing. This is the entire mechanism that keeps live editing out of the
/// browser build with no `#if`/conditional-compile split anywhere in the shared screens.
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
}

/// <summary>The desktop host's registration: live editing is always offered there.</summary>
public sealed class DesktopLiveEditingCapability : ILiveEditingCapability
{
    public bool IsAvailable => true;

    public string? TryReadLocalToken()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AbioticEditorLiveAgent", "token.txt");
            if (!File.Exists(path)) return null;
            var token = File.ReadAllText(path).Trim();
            return token.Length == 0 ? null : token;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
